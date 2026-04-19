using System;
using System.Collections.Generic;
using System.Linq;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.Memory;
using SwitchNro.Cpu.Hypervisor;
using SwitchNro.NroLoader;

namespace SwitchNro.Horizon;

/// <summary>
/// Horizon OS 模拟核心
/// 管理进程生命周期、线程调度、SVC 分发循环
/// </summary>
public sealed partial class HorizonSystem : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private readonly SvcDispatcher _svcDispatcher;
    private readonly Dictionary<ulong, HorizonProcess> _processes = new();
    private ulong _nextProcessId = 1;
    private readonly Random _rng = new();

    // ──────────────────── P1 同步原语内部状态 ────────────────────

    // WaitProcessWideKeyAtomic/SignalProcessWideKey: 进程级 futex 信号跟踪
    // Key = process_wide_key 地址，Value = 待唤醒的线程数
    private readonly Dictionary<ulong, int> _processWideKeySignals = new();

    /// <summary>SignalProcessWideKey count=-1 时使用的最大唤醒数</summary>
    private const int MaxSignalCount = 0x7FFFFFFF;

    // MapMemory 原始类型记录：UnmapMemory 时需要恢复原始 MemoryType
    // Key = (进程ID, 目标地址)，Value = (源地址, 大小, 原始 MemoryType)
    // 进程级作用域，避免进程切换时数据泄漏
    private readonly Dictionary<(ulong ProcessId, ulong DstAddr), (ulong SrcAddr, ulong Size, MemoryType OriginalType)> _mapMemoryOriginalTypes = new();

    /// <summary>当前活跃进程</summary>
    public HorizonProcess? ActiveProcess { get; private set; }

    public HorizonSystem(VirtualMemoryManager memory, SvcDispatcher svcDispatcher)
    {
        _memory = memory;
        _svcDispatcher = svcDispatcher;
    }

    /// <summary>创建新进程并加载 NRO</summary>
    public HorizonProcess CreateProcess(NroModule nroModule, ProcessInfo info, IExecutionEngine? engine = null)
    {
        var processId = _nextProcessId++;
        var processInfo = info with { ProcessId = processId };

        var process = new HorizonProcess(
            processInfo,
            _memory,
            _svcDispatcher,
            nroModule,
            engine);

        _processes[processId] = process;
        Logger.Info(nameof(HorizonSystem), $"创建进程 [{processInfo.Name}] PID={processId} 入口=0x{nroModule.EntryPoint:X16}");

        return process;
    }

    /// <summary>启动进程的主线程</summary>
    public void StartProcess(HorizonProcess process)
    {
        process.State = ProcessState.Running;
        ActiveProcess = process;

        // 获取 HVF 引擎引用（一次声明，整个方法复用）
        var hvfEngine = process.Engine as HvfExecutionEngine;

        // 设置 Homebrew ABI 环境
        // X0 = loader_config 结构指针 (指向 guest 内存中的 ConfigEntry 数组)
        // X1 = 0xFFFFFFFFFFFFFFFF (哨兵值，标识 Homebrew ABI 模式)
        // 创建主线程内核对象并注册到 HandleTable
        // 使用真实句柄而非伪句柄，NRO 可通过 CloseHandle 安全关闭
        // 分配 TLS 区域（Thread Local Storage）— 必须在创建线程之前
        // Horizon OS: 每个 TLS 区域 0x200 字节，IPC 缓冲区在 TLS + 0x100
        // TPIDR_EL0 指向 TLS 基地址
        // 预映射 64 个 TLS slot（最多 64 线程 × 0x200 = 0x8000 字节 = 32KB）
        // 注意: 同一 VirtualMemoryManager 可能复用（如测试中同一进程退出后重启），
        // 若 TLS 区域已映射则跳过（类型/权限应已正确）
        ulong tlsBase = 0x0000_0100_0000UL;
        ulong tlsTotalSize = MaxThreadCount * TlsSize; // 0x8000
        if (!_memory.IsMapped(tlsBase))
        {
            _memory.MapZero(tlsBase, tlsTotalSize, MemoryPermissions.ReadWrite, MemoryType.ThreadLocal);
            // 同步到 HVF：TLS 区域必须映射到 HVF 物理地址空间，否则 guest 访问 TPIDR_EL0 指向的内存会触发异常
            if (hvfEngine != null)
                hvfEngine.MapMemoryToHvf(tlsBase, tlsTotalSize, MemoryPermissions.ReadWrite);
        }
        process.Engine.SetTpidrEl0(tlsBase);
        process.Engine.SetTpidrroEl0(tlsBase);
        process.TlsAddress = tlsBase;

        // 创建主线程内核对象并注册到 HandleTable
        var mainThread = new KThread(process.Info.ProcessId); // 线程 ID = 进程 ID
        mainThread.TlsAddress = tlsBase; // 主线程 TLS = slot 0
        int mainThreadHandle = process.HandleTable.CreateHandle(mainThread);
        process.MainThreadHandle = mainThreadHandle;

        // 初始化线程槽跟踪：主线程占用 slot 0
        process.TryAllocateThreadSlot(); // _threadCount = 1
        process.AllocateTlsSlot(tlsBase); // _nextTlsSlot = 1（slot 0 已分配给主线程）

        var loaderConfig = new HomebrewLoaderConfig(_memory)
            .AddMainThreadHandle(mainThreadHandle)
            .AddAppletType(0) // AppletType_Application
            .AddSyscallAvailableHint(0xFFFFFFFFFFFFFFFFUL, 0xFFFFFFFFFFFFFFFFUL)
            .AddRandomSeed(0x12345678, 0x87654321)
            .AddSystemVersion(18, 1, 0); // 模拟 Horizon OS 18.1.0 (最新稳定版)
        
        // 使用一个更安全的高位地址避免与 ASLR 加载的 NRO 冲突（主线程栈后）
        ulong loaderConfigBase = 0x0000_0210_0000UL;
        if (!_memory.IsMapped(loaderConfigBase))
        {
            _memory.MapZero(loaderConfigBase, 0x1000, MemoryPermissions.ReadWrite, MemoryType.CodeStatic); // 使用 1 页
        }
        ulong loaderConfigAddr = loaderConfig.WriteToMemory(loaderConfigBase); // loader_config 区域
        process.Engine.SetRegister(0, loaderConfigAddr);
        process.Engine.SetRegister(1, 0xFFFF_FFFF_FFFF_FFFFUL); // Homebrew ABI 哨兵

        // 分配主线程栈
        ulong stackBase = 0x0000_0200_0000UL; // 规格中定义的栈区域
        // 若栈区域已映射则跳过（复用场景同 TLS）
        if (!_memory.IsMapped(stackBase))
        {
            _memory.MapZero(stackBase, process.Info.MainStackSize, MemoryPermissions.ReadWrite, MemoryType.Stack);
            // 同步到 HVF：栈必须映射到 HVF 物理地址空间，否则 guest 访问 SP 指向的内存会触发异常
            if (hvfEngine != null)
                hvfEngine.MapMemoryToHvf(stackBase, process.Info.MainStackSize, MemoryPermissions.ReadWrite);
        }
        var stackTop = stackBase + process.Info.MainStackSize;
        process.Engine.SetSP(stackTop); // ARM64 栈向下增长

        // 同步 loader_config 区域到 HVF（HomebrewLoaderConfig.WriteToMemory 已映射到 VMM，
        // 但需要额外同步到 HVF 物理地址空间，否则 guest 读取 X0 指向的内存会触发异常）
        if (hvfEngine != null)
        {
            ulong lcBase = loaderConfigAddr & ~0xFFFUL; // 4KB 对齐 (VMM 页粒度)
            ulong lcEnd = (loaderConfigAddr + (ulong)loaderConfig.TotalSize + 0xFFFUL) & ~0xFFFUL;
            ulong lcSize = lcEnd - lcBase;
            if (lcSize > 0)
                hvfEngine.MapMemoryToHvf(lcBase, lcSize, MemoryPermissions.ReadWrite);

            // 显式设置入口点 PC
            hvfEngine.SetPC(process.NroModule.EntryPoint);
            // 校验
            Console.WriteLine($"  [HVF 启动配置] PC=0x{hvfEngine.GetPC():X16}, SP=0x{hvfEngine.GetSP():X16}");
        }

        Logger.Info(nameof(HorizonSystem), $"启动进程 PID={process.Info.ProcessId}, SP=0x{stackTop:X16}, TLS=0x{tlsBase:X16}");
    }

    /// <summary>运行进程的 SVC 分发循环</summary>
    public void RunProcess(HorizonProcess process)
    {
        var engine = process.Engine;
        var result = engine.Execute(process.NroModule.EntryPoint);

        // SVC 分发循环
        while (result == ExecutionResult.SVC)
        {
            var svcInfo = engine.GetLastSvcInfo();
            var svcResult = _svcDispatcher.Dispatch(svcInfo);

            // 将结果写回 vCPU 寄存器（始终写 X0, X1, X2）
            ulong x0 = svcResult.ReturnCode.IsSuccess ? 0UL : unchecked((ulong)svcResult.ReturnCode.Value);
            engine.SetSvcResult(x0, svcResult.ReturnValue1, svcResult.ReturnValue2);

            // 继续执行
            result = engine.RunNext();
        }

        // 检查进程是否已通过 ExitProcess/ExitThread 正常退出
        // 避免覆盖已设置的 Exited 状态
        if (process.State == ProcessState.Exited)
        {
            Logger.Info(nameof(HorizonSystem), $"进程正常退出: PID={process.Info.ProcessId}");
            return;
        }

        // 清理进程级同步状态
        CleanupProcessSyncState(process.Info.ProcessId);

        if (result != ExecutionResult.NormalExit)
        {
            Logger.Warning(nameof(HorizonSystem), $"进程异常退出: PID={process.Info.ProcessId}, 原因={result}");
            process.State = ProcessState.Crashed;
        }
        else
        {
            process.State = ProcessState.Exited;
        }
    }

    /// <summary>终止进程</summary>
    public void TerminateProcess(ulong processId)
    {
        if (_processes.TryGetValue(processId, out var process))
        {
            process.Engine.RequestExit();
            process.State = ProcessState.Exiting;
            Logger.Info(nameof(HorizonSystem), $"终止进程 PID={processId}");
        }
    }

    // ──────────────────── 内存区域常量（真实 Horizon 布局） ────────────────────

    /// <summary>堆区域基地址</summary>
    public const ulong HeapBase = 0x0000_8000_0000UL;

    /// <summary>最大堆大小 (2GB)</summary>
    public const ulong HeapMaxSize = 0x8000_0000UL;

    /// <summary>栈区域基地址</summary>
    public const ulong StackBase = 0x0000_0200_0000UL;

 /// <summary>ASLR 区域基地址</summary>
 public const ulong AslrBase = 0x0000_0800_0000UL;

 /// <summary>ASLR 区域大小 (2GB)</summary>
 public const ulong AslrSize = 0x0000_8000_0000UL;

 /// <summary>Alias 区域基地址</summary>
 public const ulong AliasBase = 0x0000_4000_0000UL;

 /// <summary>Alias 区域大小 (256MB)</summary>
 public const ulong AliasSize = 0x1000_0000UL;

    /// <summary>TLS 区域大小 (每个线程 0x200 字节)</summary>
    public const ulong TlsSize = 0x200;

    /// <summary>最大线程数（与 Horizon OS 默认线程限制一致）</summary>
    private const int MaxThreadCount = 64;

    /// <summary>新线程默认栈大小 (256KB)</summary>
    private const ulong DefaultThreadStackSize = 0x40000;

    /// <summary>新线程栈分配区域基地址（主线程栈 0x0200_0000 之后）</summary>
    private const ulong ThreadStackRegionBase = 0x0000_0300_0000UL;

    /// <summary>线程优先级范围 (0-63，Horizon OS 支持 0-63)</summary>
    private const int MinThreadPriority = 0;
    private const int MaxThreadPriority = 63;

    /// <summary>处理器 ID "任意核心" 特殊值</summary>
    private const int ProcessorIdAnyCore = -2; // 0xFFFFFFFE signed

    /// <summary>当前进程伪句柄</summary>
    public const ulong CurrentProcessPseudoHandle = 0xFFFF8001UL;
    
    /// <summary>当前线程伪句柄</summary>
    public const ulong CurrentThreadPseudoHandle = 0xFFFF8000UL;

    /// <summary>
    /// 实现 SVC 0x01 SetHeapSize
    /// 在固定基地址分配或调整堆内存大小
    /// 输入: X1 = 请求的堆大小（字节）
    /// 输出: W0 = ResultCode, X1 = 堆基地址
    /// </summary>
    public SvcResult SetHeapSize(SvcInfo svc)
    {

        ulong requestedSize = svc.X1;

        if (ActiveProcess == null)
        {
            Logger.Error(nameof(HorizonSystem), "SetHeapSize: 无活跃进程");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        if (requestedSize > HeapMaxSize)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SetHeapSize: 请求大小 0x{requestedSize:X16} 超过上限 0x{HeapMaxSize:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 对齐到页面大小 (4KB, Horizon OS 标准)
        ulong alignedSize = (requestedSize + 0xFFFUL) & ~0xFFFUL;
        ulong currentSize = ActiveProcess.HeapSize;

        // 始终设置基地址（即使首次调用大小为 0）
        if (ActiveProcess.HeapAddress == 0)
            ActiveProcess.HeapAddress = HeapBase;

        if (alignedSize == currentSize)
        {
            // 大小未变，直接返回当前基地址
            Logger.Debug(nameof(HorizonSystem),
                $"SetHeapSize: 大小未变 0x{alignedSize:X16}, 基地址 0x{HeapBase:X16}");
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = HeapBase };
        }

        if (alignedSize > currentSize)
        {
            // 扩展堆：映射新页
            _memory.MapZero(HeapBase + currentSize, alignedSize - currentSize,
                MemoryPermissions.ReadWrite, MemoryType.Heap);
            // 同步到 HVF：堆内存必须映射到 HVF 物理地址空间，否则 guest 访问堆会触发异常
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
                hvfEngine.MapMemoryToHvf(HeapBase + currentSize, alignedSize - currentSize, MemoryPermissions.ReadWrite);
            Logger.Info(nameof(HorizonSystem),
                $"SetHeapSize: 扩展堆 0x{currentSize:X16} → 0x{alignedSize:X16}, 基地址 0x{HeapBase:X16}");
        }
        else
        {
            // 缩小堆：取消映射多余页
            // 同步到 HVF：先取消 HVF 映射，再取消 VMM 映射
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
                hvfEngine.UnmapMemoryFromHvf(HeapBase + alignedSize, currentSize - alignedSize);
            _memory.Unmap(HeapBase + alignedSize, currentSize - alignedSize);
            Logger.Info(nameof(HorizonSystem),
                $"SetHeapSize: 缩小堆 0x{currentSize:X16} → 0x{alignedSize:X16}, 基地址 0x{HeapBase:X16}");
        }

        ActiveProcess.HeapSize = alignedSize;

        return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = HeapBase };
    }

    // ──────────────────── SVC 0x06/0x07 ExitProcess/ExitThread ────────────────────

    /// <summary>
    /// 实现 SVC 0x06 ExitProcess
    /// 终止当前进程（所有线程立即停止）
    /// 输入: X0 = 退出码
    /// 输出: 无（进程终止）
    /// </summary>
    public SvcResult ExitProcess(SvcInfo svc)
    {
        ulong exitCode = svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 调试：打印 ExitProcess 时的现场
        Console.WriteLine($"\n[!!!] ExitProcess 被触发! PID={ActiveProcess.Info.ProcessId} 退出码=0x{exitCode:X16} PC=0x{svc.PC:X16}");
        try {
            byte[] code = new byte[32];
            _memory.Read(svc.PC - 16, code);
            Console.WriteLine($"[!!!] PC 周围机器码: {BitConverter.ToString(code).Replace("-", " ")}");
        } catch {}
        
        ActiveProcess.State = ProcessState.Exited;
        ActiveProcess.Engine.RequestExit();

        // 清理进程级同步状态（防止残留数据影响后续进程）
        CleanupProcessSyncState(ActiveProcess.Info.ProcessId);

        Logger.Info(nameof(HorizonSystem),
            $"ExitProcess: PID={ActiveProcess.Info.ProcessId} 退出码=0x{exitCode:X16}");

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    /// <summary>
    /// 实现 SVC 0x07 ExitThread
    /// 终止当前线程（当前仅主线程，等同于 ExitProcess）
    /// 输入: X0 = 退出码
    /// 输出: 无（线程终止）
    /// </summary>
    public SvcResult ExitThread(SvcInfo svc)
    {
        ulong exitCode = svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 当前只有主线程，ExitThread 等同于 ExitProcess
        // TODO: 多线程实现后，仅终止当前线程
        ActiveProcess.State = ProcessState.Exited;
        ActiveProcess.Engine.RequestExit();

        Logger.Info(nameof(HorizonSystem),
            $"ExitThread: PID={ActiveProcess.Info.ProcessId} 退出码=0x{exitCode:X16}");

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x08 SleepThread ────────────────────

    /// <summary>
    /// 实现 SVC 0x08 SleepThread
    /// 让当前线程睡眠指定纳秒数，0 或负值表示让出执行权
    /// 输入: X0 = 纳秒数
    /// 输出: 无
    /// </summary>
    public SvcResult SleepThread(SvcInfo svc)
    {
        long nanos = (long)svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        if (nanos <= 0)
        {
            // 让出执行权（yield）— 直接返回即可，无需实际等待
            Logger.Debug(nameof(HorizonSystem), "SleepThread: yield（让出执行权）");
        }
        else
        {
            // 将纳秒转换为毫秒（最小 1ms）
            int ms = (int)(nanos / 1_000_000);
            if (ms < 1) ms = 1;
            // 防止过长睡眠阻塞宿主线程（上限 100ms）
            if (ms > 100)
            {
                Logger.Warning(nameof(HorizonSystem),
                    $"SleepThread: 请求睡眠 {ms}ms 过长，截断为 100ms");
                ms = 100;
            }
            Logger.Debug(nameof(HorizonSystem), $"SleepThread: 睡眠 {ms}ms ({nanos}ns)");
            System.Threading.Thread.Sleep(ms);
        }

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x0D WaitSynchronization ────────────────────

    /// <summary>
    /// WaitSynchronization 可等待的最大句柄数（Horizon OS 限制为 0x40 = 64）
    /// </summary>
    private const int MaxWaitSyncHandles = 0x40;

    /// <summary>
    /// 实现 SVC 0x0D WaitSynchronization
    /// 等待一组内核对象中的任意一个变为信号状态
    /// 输入: X0 = 句柄数组指针（guest 内存中的 u32 数组），X1 = 句柄数量，X2 = 超时（纳秒）
    /// 输出: W0 = ResultCode, X2 = 被信号的句柄索引
    /// </summary>
    public SvcResult WaitSynchronization(SvcInfo svc)
    {
        ulong handlesAddr = svc.X0;  // 句柄数组在 guest 内存中的地址
        int numHandles = (int)svc.X1; // 句柄数量
        long timeoutNs = (long)svc.X2; // 超时纳秒（-1 = 无限等待）

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证句柄数量上限
        if (numHandles > MaxWaitSyncHandles)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"WaitSynchronization: 句柄数量 {numHandles} 超过上限 {MaxWaitSyncHandles}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncTooManyHandles) };
        }

        // 句柄数量为 0 是无效调用
        if (numHandles <= 0)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"WaitSynchronization: 句柄数量 {numHandles} 无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncInvalidHandle) };
        }

        // 从 guest 内存读取句柄数组
        var handles = new int[numHandles];
        try
        {
            for (int i = 0; i < numHandles; i++)
            {
                handles[i] = _memory.Read<int>(handlesAddr + (ulong)(i * 4));
            }
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"WaitSynchronization: 读取句柄数组失败 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 查找所有句柄对应的内核对象，验证有效性
        var waitables = new IWaitable?[numHandles];
        for (int i = 0; i < numHandles; i++)
        {
            var obj = ActiveProcess.HandleTable.GetObject(handles[i]);
            if (obj == null)
            {
                Logger.Warning(nameof(HorizonSystem),
                    $"WaitSynchronization: 无效句柄 0x{handles[i]:X8} (索引 {i})");
                // 真实 Horizon: 无效句柄返回 InvalidHandle，X2 = 出错的句柄索引
                return new SvcResult
                {
                    ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncInvalidHandle),
                    ReturnValue2 = (ulong)i
                };
            }

            if (obj is not IWaitable waitable)
            {
                Logger.Warning(nameof(HorizonSystem),
                    $"WaitSynchronization: 句柄 0x{handles[i]:X8} ({obj.ObjectType}) 不可等待");
                return new SvcResult
                {
                    ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncInvalidHandle),
                    ReturnValue2 = (ulong)i
                };
            }

            waitables[i] = waitable;
        }

        // 首次检查：是否有任何对象已信号
        // 信号优先级高于取消标志（真实 Horizon：已信号对象立即返回，无论取消状态）
        int signaledIndex = FindFirstSignaled(waitables);
        if (signaledIndex >= 0)
        {
            AutoClearIfReadableEvent(waitables[signaledIndex]);
            Logger.Debug(nameof(HorizonSystem),
                $"WaitSynchronization: 句柄 {signaledIndex} 已信号");
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue2 = (ulong)signaledIndex };
        }

        // 检查同步取消标志（仅在线程即将阻塞时检查）
        // CancelSynchronization 只中断会阻塞的同步等待，不中断已有信号的情况
        if (ActiveProcess.SyncCancelRequested)
        {
            ActiveProcess.SyncCancelRequested = false;
            Logger.Debug(nameof(HorizonSystem), "WaitSynchronization: 检测到取消标志 → WaitSyncCancelled");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncCancelled) };
        }

        // 无对象已信号，根据超时策略处理
        if (timeoutNs == 0)
        {
            // 零超时 = 非阻塞检查，无信号则立即返回 TimedOut
            Logger.Debug(nameof(HorizonSystem), "WaitSynchronization: 零超时，无信号 → TimedOut");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.TimedOut) };
        }

        // 正超时或无限超时：轮询等待
        // 注意：真实 Horizon 是事件驱动的，当前实现为简化的轮询模型
        int pollIntervalMs = 1; // 轮询间隔 1ms
        int maxWaitMs;

        if (timeoutNs < 0)
        {
            // -1 = 无限等待（设上限防止 UI 冻结）
            maxWaitMs = 100;
        }
        else
        {
            // 正超时：转换为毫秒，上限 100ms
            maxWaitMs = (int)(timeoutNs / 1_000_000);
            if (maxWaitMs < 1) maxWaitMs = 1;
            if (maxWaitMs > 100) maxWaitMs = 100;
        }

        int waited = 0;
        while (waited < maxWaitMs)
        {
            System.Threading.Thread.Sleep(pollIntervalMs);
            waited += pollIntervalMs;

            signaledIndex = FindFirstSignaled(waitables);
            if (signaledIndex >= 0)
            {
                AutoClearIfReadableEvent(waitables[signaledIndex]);
                // 信号优先：不清除取消标志，让下次同步调用处理
                Logger.Debug(nameof(HorizonSystem),
                    $"WaitSynchronization: 句柄 {signaledIndex} 信号（等待 {waited}ms）");
                return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue2 = (ulong)signaledIndex };
            }

            // 无信号时检查取消标志（CancelSynchronization 只中断阻塞等待）
            if (ActiveProcess.SyncCancelRequested)
            {
                ActiveProcess.SyncCancelRequested = false;
                Logger.Debug(nameof(HorizonSystem),
                    $"WaitSynchronization: 轮询期间检测到取消标志（等待 {waited}ms） → WaitSyncCancelled");
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncCancelled) };
            }

        }

        // 超时
        Logger.Debug(nameof(HorizonSystem), $"WaitSynchronization: 超时（等待 {maxWaitMs}ms）");
        return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.TimedOut) };
    }

    /// <summary>
    /// 在可等待对象数组中查找第一个已信号的对象
    /// </summary>
    private static int FindFirstSignaled(IWaitable?[] waitables)
    {
        for (int i = 0; i < waitables.Length; i++)
        {
            if (waitables[i]?.IsSignaled == true)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 如果被信号的对象是 KReadableEvent，自动清除信号
    /// 真实 Horizon 中 KReadableEvent 是 auto-clear 类型，
    /// WaitSynchronization 成功返回后会自动清除信号
    /// </summary>
    private static void AutoClearIfReadableEvent(IWaitable? waitable)
    {
        if (waitable is KReadableEvent readableEvent)
            readableEvent.IsSignaled = false;
    }

    // ──────────────────── SVC 0x0E CancelSynchronization ────────────────────

    /// <summary>
    /// 实现 SVC 0x0E CancelSynchronization
    /// 取消指定线程的同步等待
    /// 输入: W0 = 线程句柄
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 如果目标线程当前正在 WaitSynchronization / ReplyAndReceive 中，
    ///   该调用将立即返回 WaitSyncCancelled (0xEC01)
    /// - 如果目标线程未在同步等待中，设置取消标志，
    ///   下一次同步调用将立即返回 WaitSyncCancelled
    /// - 取消标志检查后自动清除
    /// </summary>
    public SvcResult CancelSynchronization(SvcInfo svc)
    {
        int threadHandle = (int)svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 当前单线程模型：仅支持取消当前进程的主线程
        // 真实 Horizon 需要通过线程句柄查找 KThread 对象
        // 当前伪句柄 0xFFFF8000 = 当前进程，等价于主线程
        // 句柄 0 不是有效线程句柄
        if (threadHandle == 0)
        {
            Logger.Warning(nameof(HorizonSystem), "CancelSynchronization: 句柄为 0，无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        if (threadHandle != unchecked((int)CurrentProcessPseudoHandle))
        {
            // 尝试在 HandleTable 中查找句柄
            var obj = ActiveProcess.HandleTable.GetObject(threadHandle);
            if (obj == null)
            {
                Logger.Warning(nameof(HorizonSystem),
                    $"CancelSynchronization: 无效句柄 0x{threadHandle:X8}");
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
            }

            // TODO: 多线程实现后需要验证句柄类型为 KThread
            // 当前单线程模型，任何有效句柄都视为可接受
        }

        // 设置取消标志
        ActiveProcess.SyncCancelRequested = true;

        Logger.Debug(nameof(HorizonSystem),
            $"CancelSynchronization: 已设置取消标志 (handle=0x{threadHandle:X8})");
        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x0F/0x10 ArbitrateLock/ArbitrateUnlock ────────────────────

    /// <summary>
    /// 实现 SVC 0x0F ArbitrateLock
    /// 原子地获取用户态互斥锁（基于地址的轻量级锁）
    /// 输入: X0 = 当前线程句柄, X1 = 互斥锁地址, X2 = 请求者 tag
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 读取 mutex_addr 处的 u32 值
    /// - 如果值 == 0（未锁定），原子写入 tag 并返回 Success
    /// - 如果值 != 0（已被持有），返回 ConcurrentConflict（需要用户态重试）
    /// - 真实 Horizon 会将等待线程挂起，当前实现为非阻塞模式
    /// </summary>
    public SvcResult ArbitrateLock(SvcInfo svc)
    {
        int threadHandle = (int)svc.X0;
        ulong mutexAddr = svc.X1;
        uint tag = (uint)svc.X2;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证线程句柄
        // 当前单线程模型：主线程伪句柄 = 0xFFFF8000 或 HandleTable 中的线程句柄
        // TODO: 多线程实现后需要验证为 KThread 类型的句柄
        if (threadHandle != 0 && threadHandle != unchecked((int)CurrentProcessPseudoHandle))
        {
            if (!ActiveProcess.HandleTable.IsValid(threadHandle))
            {
                Logger.Warning(nameof(HorizonSystem),
                    $"ArbitrateLock: 无效线程句柄 0x{threadHandle:X8}");
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
            }
        }

        // 验证地址在进程空间内且已映射
        if (!IsInProcessAddressSpace(mutexAddr, 4))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"ArbitrateLock: 地址超出进程空间 0x{mutexAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 原子地获取锁：直接读取 guest 内存中的互斥锁值
        // 不使用额外的内核跟踪字典，避免与 guest 内存状态不一致
        try
        {
            uint currentValue = _memory.Read<uint>(mutexAddr);
            if (currentValue != 0)
            {
                // 互斥锁已被持有（guest 值非 0）
                // 真实 Horizon 会将线程挂起等待；当前实现直接返回冲突
                Logger.Debug(nameof(HorizonSystem),
                    $"ArbitrateLock: 互斥锁 0x{mutexAddr:X16} 已被持有 (value=0x{currentValue:X8})");
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.ConcurrentConflict) };
            }

            // 原子写入 tag 获取锁
            _memory.Write(mutexAddr, BitConverter.GetBytes(tag));

            Logger.Debug(nameof(HorizonSystem),
                $"ArbitrateLock: 获取互斥锁 0x{mutexAddr:X16} tag=0x{tag:X8}");
            return new SvcResult { ReturnCode = ResultCode.Success };
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"ArbitrateLock: 内存访问错误 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }
    }

    /// <summary>
    /// 实现 SVC 0x10 ArbitrateUnlock
    /// 释放用户态互斥锁
    /// 输入: X1 = 互斥锁地址
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 将 mutex_addr 处的 u32 值置为 0（释放锁）
    /// - 从内核跟踪中移除锁持有者
    /// - 真实 Horizon 会唤醒等待此锁的线程
    /// </summary>
    public SvcResult ArbitrateUnlock(SvcInfo svc)
    {
        ulong mutexAddr = svc.X1;  // 注意：真实 Horizon 中地址在 X1，不是 X0

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证地址在进程空间内
        if (!IsInProcessAddressSpace(mutexAddr, 4))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"ArbitrateUnlock: 地址超出进程空间 0x{mutexAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        try
        {
            // 将 guest 内存中的互斥锁值置为 0（释放）
            _memory.Write(mutexAddr, BitConverter.GetBytes(0U));

            Logger.Debug(nameof(HorizonSystem),
                $"ArbitrateUnlock: 释放互斥锁 0x{mutexAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.Success };
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"ArbitrateUnlock: 内存访问错误 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }
    }

    // ──────────────────── SVC 0x11/0x12 WaitProcessWideKeyAtomic/SignalProcessWideKey ────────────────────

    /// <summary>
    /// 实现 SVC 0x11 WaitProcessWideKeyAtomic
    /// 原子地等待进程级键（futex 语义）
    /// 输入: X0 = 等待地址, X1 = process_wide_key, X2 = tag, X3 = 超时（纳秒）
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 原子地检查 address 处的 u32 值是否 == tag
    /// - 如果 != tag，立即返回 Success（值已变化，无需等待）
    /// - 如果 == tag，等待 SignalProcessWideKey 唤醒或超时
    /// - 被唤醒后返回 Success
    /// - 超时返回 TimedOut
    /// </summary>
    public SvcResult WaitProcessWideKeyAtomic(SvcInfo svc)
    {
        ulong address = svc.X0;       // 等待地址
        ulong key = svc.X1;           // process_wide_key（futex 键）
        uint tag = (uint)svc.X2;      // 期望值
        long timeoutNs = (long)svc.X3; // 超时纳秒（-1 = 无限）

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证地址在进程空间内
        if (!IsInProcessAddressSpace(address, 4))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"WaitProcessWideKeyAtomic: 地址超出进程空间 0x{address:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证 key 在进程空间内
        if (key != 0 && !IsInProcessAddressSpace(key, 4))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"WaitProcessWideKeyAtomic: key 超出进程空间 0x{key:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        try
        {
            // 原子检查：读取 address 处的 u32 值
            uint currentValue = _memory.Read<uint>(address);
            if (currentValue != tag)
            {
                // 值已变化，无需等待
                Logger.Debug(nameof(HorizonSystem),
                    $"WaitProcessWideKeyAtomic: 0x{address:X16} 值 0x{currentValue:X8} != 0x{tag:X8}，无需等待");
                return new SvcResult { ReturnCode = ResultCode.Success };
            }
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"WaitProcessWideKeyAtomic: 读取失败 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 值 == tag，需要等待
        if (timeoutNs == 0)
        {
            // 零超时：非阻塞检查，值未变则返回 TimedOut
            Logger.Debug(nameof(HorizonSystem),
                "WaitProcessWideKeyAtomic: 零超时，值未变 → TimedOut");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.TimedOut) };
        }

        // 检查同步取消标志（在线程即将阻塞前检查，与 WaitSynchronization 一致）
        if (ActiveProcess.SyncCancelRequested)
        {
            ActiveProcess.SyncCancelRequested = false;
            Logger.Debug(nameof(HorizonSystem),
                "WaitProcessWideKeyAtomic: 检测到取消标志 → WaitSyncCancelled");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncCancelled) };
        }

        // 正超时或无限超时：轮询等待 key 信号
        int maxWaitMs;
        if (timeoutNs < 0)
            maxWaitMs = 100; // 无限等待上限
        else
        {
            maxWaitMs = (int)(timeoutNs / 1_000_000);
            if (maxWaitMs < 1) maxWaitMs = 1;
            if (maxWaitMs > 100) maxWaitMs = 100;
        }

        int waited = 0;
        while (waited < maxWaitMs)
        {
            System.Threading.Thread.Sleep(1);
            waited++;

            // 检查是否有信号可用
            if (_processWideKeySignals.TryGetValue(key, out var signalCount) && signalCount > 0)
            {
                // 消耗一个信号
                _processWideKeySignals[key] = signalCount - 1;
                if (_processWideKeySignals[key] == 0)
                    _processWideKeySignals.Remove(key);

                Logger.Debug(nameof(HorizonSystem),
                    $"WaitProcessWideKeyAtomic: key=0x{key:X16} 信号唤醒（等待 {waited}ms）");
                return new SvcResult { ReturnCode = ResultCode.Success };
            }

            // 也检查地址值是否已变（可能是其他线程修改）
            try
            {
                uint currentValue = _memory.Read<uint>(address);
                if (currentValue != tag)
                {
                    Logger.Debug(nameof(HorizonSystem),
                        $"WaitProcessWideKeyAtomic: 0x{address:X16} 值已变（等待 {waited}ms）");
                    return new SvcResult { ReturnCode = ResultCode.Success };
                }
            }
            catch { break; } // 内存访问失败，退出等待

            // 无信号/值未变时检查取消标志
            if (ActiveProcess.SyncCancelRequested)
            {
                ActiveProcess.SyncCancelRequested = false;
                Logger.Debug(nameof(HorizonSystem),
                    $"WaitProcessWideKeyAtomic: 轮询期间检测到取消标志（等待 {waited}ms） → WaitSyncCancelled");
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.WaitSyncCancelled) };
            }
        }

        Logger.Debug(nameof(HorizonSystem),
            $"WaitProcessWideKeyAtomic: 超时（等待 {maxWaitMs}ms）");
        return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.TimedOut) };
    }

    /// <summary>
    /// 实现 SVC 0x12 SignalProcessWideKey
    /// 唤醒等待指定 process_wide_key 的线程（futex wake 语义）
    /// 输入: X0 = process_wide_key, X1 = 唤醒数量（-1 = 全部）
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 唤醒最多 count 个等待此 key 的线程
    /// - count = -1 表示唤醒所有等待线程
    /// - 当前单线程实现：记录信号计数，供 WaitProcessWideKeyAtomic 轮询检测
    /// </summary>
    public SvcResult SignalProcessWideKey(SvcInfo svc)
    {
        ulong key = svc.X0;
        int count = (int)svc.X1;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // count 必须为正数或 -1
        if (count < -1 || count == 0)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SignalProcessWideKey: 无效唤醒数量 {count}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidCount) };
        }

        // 将信号记入字典（单线程模型中，信号供下次 WaitProcessWideKeyAtomic 检测）
        // 真实 Horizon 中，内核直接唤醒等待线程；这里记录待唤醒数
        if (count == -1)
        {
            // 唤醒所有：设置最大唤醒计数
            _processWideKeySignals[key] = MaxSignalCount;
        }
        else
        {
            // 增量累加（多次 Signal 可叠加）
            _processWideKeySignals.TryGetValue(key, out var existing);
            _processWideKeySignals[key] = existing + count;
        }

        Logger.Debug(nameof(HorizonSystem),
            $"SignalProcessWideKey: key=0x{key:X16} count={count} → 待唤醒={_processWideKeySignals.GetValueOrDefault(key)}");
        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x19 CloseHandle ────────────────────

    /// <summary>
    /// 实现 SVC 0x19 CloseHandle
    /// 关闭内核对象句柄，释放对应的内核对象引用
    /// 输入: W0 = 句柄 ID
    /// 输出: W0 = ResultCode
    /// </summary>
    public SvcResult CloseHandle(SvcInfo svc)
    {
        int handle = (int)svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 句柄 0 无效
        if (handle == 0)
        {
            Logger.Warning(nameof(HorizonSystem), "CloseHandle: 句柄为 0，无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 伪句柄不可关闭（0xFFFF8000 = 当前进程）
        if (handle == unchecked((int)CurrentProcessPseudoHandle))
        {
            Logger.Warning(nameof(HorizonSystem),
                "CloseHandle: 不可关闭当前进程伪句柄 0xFFFF8000");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        if (!ActiveProcess.HandleTable.CloseHandle(handle))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"CloseHandle: 无效句柄 0x{handle:X8}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        Logger.Debug(nameof(HorizonSystem), $"CloseHandle: 关闭句柄 0x{handle:X8}");
        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x34 CreateThread ────────────────────

    /// <summary>
    /// 下一个可分配的线程栈地址（从 ThreadStackRegionBase 递增分配）
    /// </summary>
    private ulong _nextThreadStackAddr = ThreadStackRegionBase;

    /// <summary>
    /// 实现 SVC 0x34 CreateThread
    /// 创建新的内核线程对象并返回其句柄
    /// 输入: X0=entry_point, X1=argument, X2=stack_top, X3=priority, X4=processor_id
    /// 输出: W0=ResultCode, X1=thread_handle (成功时)
    /// 
    /// 语义：
    /// - 创建 KThread 内核对象，分配 TLS slot 和栈内存
    /// - 注册到进程 HandleTable，返回线程句柄
    /// - 当前单线程模型：线程处于 Created 状态，不实际执行
    ///   NRO 需通过 StartThread (SVC 0x4C) 启动线程（未来实现）
    /// - 线程句柄可用于 CloseHandle、WaitSynchronization 等
    /// </summary>
    public SvcResult CreateThread(SvcInfo svc)
    {
        ulong entryPoint = svc.X0;
        ulong argument = svc.X1;
        ulong stackTop = svc.X2;
        int priority = (int)svc.X3;
        int processorId = (int)svc.X4;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证优先级范围 (0-63)
        if (priority < MinThreadPriority || priority > MaxThreadPriority)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"CreateThread: 无效优先级 {priority} (有效范围 {MinThreadPriority}-{MaxThreadPriority})");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidPriority) };
        }

        // 验证处理器 ID
        // 有效值: 0-3 (核心亲和性) 或 -2 (任意核心 = 0xFFFFFFFE)
        // -1 也是一个特殊值（某些 Horizon 版本中意为"默认核心"）
        if (processorId != ProcessorIdAnyCore && processorId != -1
            && (processorId < 0 || processorId > 3))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"CreateThread: 无效处理器 ID {processorId} (有效: 0-3, -1=默认, -2=任意)");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidProcessorId) };
        }

        // 验证入口点地址在进程空间内
        if (entryPoint == 0 || !IsInProcessAddressSpace(entryPoint, 4))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"CreateThread: 入口点 0x{entryPoint:X16} 不在进程地址空间内");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证栈顶地址
        // stack_top 应该是已分配栈区域的末尾地址（ARM64 栈向下增长）
        // 真实 Horizon 不验证栈地址，但我们需要确保它在进程空间内
        if (stackTop != 0 && !IsInProcessAddressSpace(stackTop, 4))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"CreateThread: 栈顶 0x{stackTop:X16} 不在进程地址空间内");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 检查线程数量限制
        if (!ActiveProcess.TryAllocateThreadSlot())
        {
            Logger.Warning(nameof(HorizonSystem),
                $"CreateThread: 已达线程上限 ({MaxThreadCount})");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.OutOfResource) };
        }

        // 分配 TLS slot
        ulong tlsAddress = ActiveProcess.AllocateTlsSlot(ActiveProcess.TlsAddress);

        // 分配线程栈
        // 真实 Horizon: NRO 自己分配栈内存并通过 stack_top 传入
        // 但 homebrew 通常传入 stack_top=0，由内核分配默认栈
        ulong stackBase;
        ulong stackSize;
        if (stackTop == 0)
        {
            // 内核分配默认栈
            stackSize = DefaultThreadStackSize;
            stackBase = _nextThreadStackAddr;
            _nextThreadStackAddr += stackSize;
            stackTop = stackBase + stackSize;

            // 映射栈内存
            _memory.MapZero(stackBase, stackSize, MemoryPermissions.ReadWrite, MemoryType.Stack);
            // 同步到 HVF：线程栈必须映射到 HVF 物理地址空间
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
                hvfEngine.MapMemoryToHvf(stackBase, stackSize, MemoryPermissions.ReadWrite);
        }
        else
        {
            // NRO 已分配栈，stack_top 指向栈区域末尾
            // 注意：我们无法确定栈的实际基地址和大小，这是已知简化
            // TODO: 真实 Horizon 中 NRO 通过 Additional Type 2 传入栈大小，
            //       当前未解析此参数，使用默认值作为最佳推测
            stackBase = stackTop - DefaultThreadStackSize;
            stackSize = DefaultThreadStackSize;
        }

        // 创建 KThread 内核对象
        ulong threadId = ActiveProcess.AllocateThreadId();
        var thread = new KThread(threadId, entryPoint, argument, stackTop,
            stackBase, stackSize, priority, processorId)
        {
            TlsAddress = tlsAddress
        };

        // 注册到 HandleTable
        int threadHandle = ActiveProcess.HandleTable.CreateHandle(thread);

        Logger.Info(nameof(HorizonSystem),
            $"CreateThread: handle=0x{threadHandle:X8} tid={threadId} entry=0x{entryPoint:X16} " +
            $"arg=0x{argument:X16} sp=0x{stackTop:X16} pri={priority} core={processorId} tls=0x{tlsAddress:X16}");

        return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = (ulong)threadHandle };
    }

    // ──────────────────── SVC 0x4C StartThread ────────────────────

    /// <summary>
    /// 实现 SVC 0x4C StartThread
    /// 启动已创建的线程（Created → Running 状态转换）
    /// 输入: X0 = 线程句柄
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 将 KThread 从 Created 状态转换为 Running 状态
    /// - 真实 Horizon 中，线程被加入调度队列并开始执行：
    ///   PC=EntryPoint, X0=Argument, SP=StackTop, TPIDR_EL0=TlsAddress
    /// - 当前单 vCPU 模型限制：
    ///   线程状态正确转换为 Running，但无法实际并发执行
    ///   子线程的入口函数不会被执行
    ///   这是已知限制，待多 vCPU 实现后解决
    /// - 对已 Running 的线程调用返回 InvalidState
    /// - 对已 Terminated 的线程调用返回 InvalidState
    /// - 无效句柄/非 KThread 句柄返回 InvalidHandle
    /// </summary>
    public SvcResult StartThread(SvcInfo svc)
    {
        int threadHandle = (int)svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 句柄为 0 无效
        if (threadHandle == 0)
        {
            Logger.Warning(nameof(HorizonSystem), "StartThread: 句柄为 0，无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 支持当前进程伪句柄 (0xFFFF8001) 或当前线程伪句柄 (0xFFFF8000)
        // 单线程模型中等价于主线程
        if (threadHandle == unchecked((int)CurrentThreadPseudoHandle) || threadHandle == unchecked((int)CurrentProcessPseudoHandle))
        {
            Logger.Warning(nameof(HorizonSystem),
                "StartThread: 当前进程伪句柄指向主线程，已是 Running 状态");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 查找 KThread 对象
        var thread = ActiveProcess.HandleTable.GetObject<KThread>(threadHandle);
        if (thread == null)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"StartThread: 无效句柄 0x{threadHandle:X8} 或非 KThread 类型");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 验证线程状态 — 只能启动处于 Created 状态的线程
        if (thread.State != ThreadState.Created)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"StartThread: 线程 0x{threadHandle:X8} 状态为 {thread.State}，只有 Created 状态可启动");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 状态转换: Created → Running
        thread.State = ThreadState.Running;

        // 真实 Horizon 中，内核会：
        // 1. 设置子线程的 CPU 上下文：PC=EntryPoint, X0=Argument, SP=StackTop, TPIDR_EL0=TlsAddress
        // 2. 将子线程加入调度队列
        // 3. 子线程在调度到的时间片内并发执行
        //
        // 当前单 vCPU 模型限制：
        // - 只有一个 HvfExecutionEngine (vCPU)，无法同时运行多个线程
        // - 子线程状态正确标记为 Running，但入口函数不会被执行
        // - 子线程永远不会自然终止（不会变为 Terminated 状态）
        // - WaitSynchronization 等待此线程句柄将永远超时
        //
        // TODO: 多 vCPU 实现（每个 KThread 分配独立 vCPU）后，
        //       在此创建新执行引擎并启动线程函数

        Logger.Info(nameof(HorizonSystem),
            $"StartThread: handle=0x{threadHandle:X8} tid={thread.ThreadId} " +
            $"entry=0x{thread.EntryPoint:X16} arg=0x{thread.Argument:X16} " +
            $"sp=0x{thread.StackTop:X16} tls=0x{thread.TlsAddress:X16} → Running");

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x40 SetThreadActivity ────────────────────

    /// <summary>
    /// 实现 SVC 0x40 SetThreadActivity
    /// 设置线程的活动状态（暂停/恢复）
    /// 输入: X0 = 线程句柄, X1 = ThreadActivity (0=Runnable, 1=Paused)
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - ThreadActivity.Runnable (0): 将暂停的线程恢复为可调度状态 (Paused → Running)
    /// - ThreadActivity.Paused (1): 将运行中的线程暂停 (Running → Paused)
    /// - 无效的 Activity 值返回 InvalidThreadActivity
    /// - 对 Created/Terminated 状态的线程调用返回 InvalidState
    /// - 线程已处于目标状态时返回 InvalidState（如对已暂停线程再暂停）
    /// - 无效句柄/非 KThread 句柄返回 InvalidHandle
    /// - 当前单 vCPU 模型：暂停/恢复仅更新线程状态元数据，不影响实际执行
    /// </summary>
    public SvcResult SetThreadActivity(SvcInfo svc)
    {
        int threadHandle = (int)svc.X0;
        int activityValue = (int)svc.X1;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证 Activity 值
        if (activityValue < 0 || activityValue > 1)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SetThreadActivity: 无效 Activity 值 {activityValue} (有效: 0=Runnable, 1=Paused)");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidThreadActivity) };
        }

        var activity = (ThreadActivity)activityValue;

        // 句柄为 0 无效
        if (threadHandle == 0)
        {
            Logger.Warning(nameof(HorizonSystem), "SetThreadActivity: 句柄为 0，无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 支持当前进程伪句柄 (0xFFFF8000) — 单线程模型中等价于主线程
        if (threadHandle == unchecked((int)CurrentProcessPseudoHandle))
        {
            var mainThread = ActiveProcess.HandleTable.GetObject<KThread>(ActiveProcess.MainThreadHandle);
            if (mainThread == null)
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };

            return ApplyThreadActivity(mainThread, activity, threadHandle);
        }

        // 查找 KThread 对象
        var thread = ActiveProcess.HandleTable.GetObject<KThread>(threadHandle);
        if (thread == null)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SetThreadActivity: 无效句柄 0x{threadHandle:X8} 或非 KThread 类型");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        return ApplyThreadActivity(thread, activity, threadHandle);
    }

    /// <summary>
    /// 将 ThreadActivity 应用到 KThread，执行状态转换
    /// </summary>
    private SvcResult ApplyThreadActivity(KThread thread, ThreadActivity activity, int threadHandle)
    {
        // Created 和 Terminated 状态的线程不可暂停/恢复
        if (thread.State == ThreadState.Created || thread.State == ThreadState.Terminated)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SetThreadActivity: 线程 0x{threadHandle:X8} 状态为 {thread.State}，不可设置 Activity");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        switch (activity)
        {
            case ThreadActivity.Runnable:
                if (thread.State != ThreadState.Paused)
                {
                    // 线程已是 Runnable（Running），重复恢复返回 InvalidState
                    Logger.Warning(nameof(HorizonSystem),
                        $"SetThreadActivity: 线程 0x{threadHandle:X8} 已是 {thread.State}，无需恢复");
                    return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
                }
                // Paused → Running
                thread.State = ThreadState.Running;
                Logger.Info(nameof(HorizonSystem),
                    $"SetThreadActivity: handle=0x{threadHandle:X8} → Runnable (Paused → Running)");
                break;

            case ThreadActivity.Paused:
                if (thread.State != ThreadState.Running)
                {
                    // 线程已是 Paused，重复暂停返回 InvalidState
                    Logger.Warning(nameof(HorizonSystem),
                        $"SetThreadActivity: 线程 0x{threadHandle:X8} 已是 {thread.State}，无需暂停");
                    return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
                }
                // Running → Paused
                thread.State = ThreadState.Paused;
                Logger.Info(nameof(HorizonSystem),
                    $"SetThreadActivity: handle=0x{threadHandle:X8} → Paused (Running → Paused)");
                break;

            default:
                // 不应到达（上方已验证 activityValue 0-1）
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidThreadActivity) };
        }

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x09/0x0A GetThreadPriority/SetThreadPriority ────────────────────

    /// <summary>
    /// 实现 SVC 0x09 GetThreadPriority
    /// 获取指定线程的优先级
    /// 输入: X0 = 线程句柄
    /// 输出: W0 = ResultCode, X1 = 优先级 (成功时)
    /// 
    /// 语义：
    /// - 从 HandleTable 中查找 KThread 对象
    /// - 返回线程的当前优先级 (0-63)
    /// - 无效句柄返回 InvalidHandle
    /// - 句柄类型不是 KThread 返回 InvalidHandle
    /// </summary>
    public SvcResult GetThreadPriority(SvcInfo svc)
    {
        int threadHandle = (int)svc.X0;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 句柄为 0 无效
        if (threadHandle == 0)
        {
            Logger.Warning(nameof(HorizonSystem), "GetThreadPriority: 句柄为 0，无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 支持当前进程伪句柄 (0xFFFF8000)
        // 注意：0xFFFF8000 是 "当前进程" 伪句柄，不是 "当前线程" 伪句柄
        // 真实 Horizon 有 CurrentThreadPseudoHandle (0xFFFF8001)，当前未实现
        // 单线程模型中，当前进程伪句柄等价于主线程
        if (threadHandle == unchecked((int)CurrentProcessPseudoHandle))
        {
            var mainThread = ActiveProcess.HandleTable.GetObject<KThread>(ActiveProcess.MainThreadHandle);
            if (mainThread != null)
                return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = (ulong)mainThread.Priority };

            // 主线程句柄无效（不应发生）
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 查找 KThread 对象
        var thread = ActiveProcess.HandleTable.GetObject<KThread>(threadHandle);
        if (thread == null)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"GetThreadPriority: 无效句柄 0x{threadHandle:X8} 或非 KThread 类型");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        Logger.Debug(nameof(HorizonSystem),
            $"GetThreadPriority: handle=0x{threadHandle:X8} → priority={thread.Priority}");

        return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = (ulong)thread.Priority };
    }

    /// <summary>
    /// 实现 SVC 0x0A SetThreadPriority
    /// 设置指定线程的优先级
    /// 输入: X0 = 线程句柄, X1 = 新优先级
    /// 输出: W0 = ResultCode
    /// 
    /// 语义：
    /// - 修改 KThread 对象的 Priority 属性
    /// - 优先级范围 0-63，超出范围返回 InvalidPriority
    /// - 无效句柄返回 InvalidHandle
    /// - 真实 Horizon 中，修改优先级会影响调度顺序
    ///   当前单线程模型中仅更新元数据，不影响执行
    /// </summary>
    public SvcResult SetThreadPriority(SvcInfo svc)
    {
        int threadHandle = (int)svc.X0;
        int priority = (int)svc.X1;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证优先级范围 (0-63)
        if (priority < MinThreadPriority || priority > MaxThreadPriority)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SetThreadPriority: 无效优先级 {priority} (有效范围 {MinThreadPriority}-{MaxThreadPriority})");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidPriority) };
        }

        // 句柄为 0 无效
        if (threadHandle == 0)
        {
            Logger.Warning(nameof(HorizonSystem), "SetThreadPriority: 句柄为 0，无效");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 支持当前进程伪句柄 (0xFFFF8000) — 单线程模型中等价于主线程
        // 注意：真实 Horizon 有 CurrentThreadPseudoHandle (0xFFFF8001)，当前未实现
        if (threadHandle == unchecked((int)CurrentProcessPseudoHandle))
        {
            var mainThread = ActiveProcess.HandleTable.GetObject<KThread>(ActiveProcess.MainThreadHandle);
            if (mainThread != null)
            {
                mainThread.Priority = priority;
                Logger.Debug(nameof(HorizonSystem),
                    $"SetThreadPriority: 主线程优先级 → {priority}");
                return new SvcResult { ReturnCode = ResultCode.Success };
            }
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        // 查找 KThread 对象
        var thread = ActiveProcess.HandleTable.GetObject<KThread>(threadHandle);
        if (thread == null)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"SetThreadPriority: 无效句柄 0x{threadHandle:X8} 或非 KThread 类型");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        thread.Priority = priority;

        Logger.Debug(nameof(HorizonSystem),
            $"SetThreadPriority: handle=0x{threadHandle:X8} → priority={priority}");

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x05 QueryMemory ────────────────────

    /// <summary>
    /// 实现 SVC 0x05 QueryMemory
    /// 查询虚拟地址的内存区域信息
    /// 输入: X0 = MemoryInfo 缓冲区指针, X2 = 查询地址
    /// 输出: W0 = ResultCode, X2 = PageInfo (0 = 无特殊属性)
    /// </summary>
    public SvcResult QueryMemory(SvcInfo svc)
    {
        ulong infoAddr = svc.X0;  // MemoryInfo 输出缓冲区地址
        ulong queryAddr = svc.X2; // 查询的虚拟地址

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        try
        {
            var info = _memory.QueryMemoryInfo(queryAddr);

            // 将 MemoryInfo 写入 guest 内存（真实 Horizon 布局：40 字节）
            // +0x00: BaseAddress (u64)
            // +0x08: Size (u64)
            // +0x10: Type (u32)
            // +0x14: Attribute (u32)
            // +0x18: Permission (u32)
            // +0x1C: IpcRefCount (u32)
            _memory.Write(infoAddr, BitConverter.GetBytes(info.BaseAddress));
            _memory.Write(infoAddr + 8, BitConverter.GetBytes(info.Size));
            _memory.Write(infoAddr + 16, BitConverter.GetBytes(info.Type));
            _memory.Write(infoAddr + 20, BitConverter.GetBytes(info.Attribute));
            _memory.Write(infoAddr + 24, BitConverter.GetBytes(info.Permission));
            _memory.Write(infoAddr + 28, BitConverter.GetBytes(info.IpcRefCount));
            _memory.Write(infoAddr + 32, BitConverter.GetBytes(info.DeviceRefCount));

            Logger.Debug(nameof(HorizonSystem),
                $"QueryMemory: 0x{queryAddr:X16} → base=0x{info.BaseAddress:X16} size=0x{info.Size:X16} type={info.Type} perm={info.Permission}");

            // PageInfo = 0（无特殊页属性），返回在 X2
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue2 = 0 };
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"QueryMemory: 内存访问错误 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }
    }

    // ──────────────────── SVC 0x03/0x04 MapMemory/UnmapMemory ────────────────────

    /// <summary>
    /// 实现 SVC 0x03 MapMemory
    /// 将源地址的内存重映射到目标地址并更改权限
    /// 输入: X0 = 目标地址, X1 = 源地址, X2 = 大小
    /// 输出: W0 = ResultCode
    /// </summary>
    public SvcResult MapMemory(SvcInfo svc)
    {
        ulong dstAddr = svc.X0;
        ulong srcAddr = svc.X1;
        ulong size = svc.X2;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证对齐
        if (!IsPageAligned(srcAddr) || !IsPageAligned(dstAddr) || !IsPageAligned(size) || size == 0)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapMemory: 地址或大小未对齐 src=0x{srcAddr:X16} dst=0x{dstAddr:X16} size=0x{size:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证地址范围在进程空间内
        if (!IsInProcessAddressSpace(srcAddr, size) || !IsInProcessAddressSpace(dstAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapMemory: 地址超出进程空间 src=0x{srcAddr:X16} dst=0x{dstAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证源地址完全映射（防止部分重映射导致状态不一致）
        if (!IsRegionFullyMapped(srcAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapMemory: 源地址未完全映射 src=0x{srcAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 验证目标地址未映射（防止覆盖/泄漏物理页）
        if (IsRegionMapped(dstAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapMemory: 目标地址已映射 dst=0x{dstAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 验证源和目标范围不重叠
        if (RangesOverlap(srcAddr, size, dstAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapMemory: 源和目标范围重叠 src=0x{srcAddr:X16} dst=0x{dstAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        try
        {
            // 保存源地址的原始 MemoryType（UnmapMemory 时需要恢复）
            var originalType = _memory.QueryMemoryInfo(srcAddr).Type;
            _mapMemoryOriginalTypes[(ActiveProcess.Info.ProcessId, dstAddr)] = (srcAddr, size, (MemoryType)originalType);

            // 重映射：物理页从 src 搬到 dst，目标权限为 RW，目标类型为 Alias
            _memory.Remap(srcAddr, dstAddr, size, MemoryPermissions.ReadWrite, MemoryType.Alias);

            // 同步 HVF 映射：先取消源映射，再映射目标
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
            {
                hvfEngine.UnmapMemoryFromHvf(srcAddr, size);
                hvfEngine.MapMemoryToHvf(dstAddr, size, MemoryPermissions.ReadWrite);
            }

            Logger.Info(nameof(HorizonSystem),
                $"MapMemory: 0x{srcAddr:X16} → 0x{dstAddr:X16} 大小=0x{size:X16} origType={originalType}");
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"MapMemory: 内存访问错误 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    /// <summary>
    /// 实现 SVC 0x04 UnmapMemory
    /// 将目标地址的内存移回源地址（MapMemory 的逆操作）
    /// 输入: X0 = 目标地址, X1 = 源地址, X2 = 大小
    /// 输出: W0 = ResultCode
    /// </summary>
    public SvcResult UnmapMemory(SvcInfo svc)
    {
        ulong dstAddr = svc.X0;
        ulong srcAddr = svc.X1;
        ulong size = svc.X2;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证对齐
        if (!IsPageAligned(srcAddr) || !IsPageAligned(dstAddr) || !IsPageAligned(size) || size == 0)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapMemory: 地址或大小未对齐 src=0x{srcAddr:X16} dst=0x{dstAddr:X16} size=0x{size:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证地址范围
        if (!IsInProcessAddressSpace(srcAddr, size) || !IsInProcessAddressSpace(dstAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapMemory: 地址超出进程空间 src=0x{srcAddr:X16} dst=0x{dstAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证目标地址完全映射（必须存在才能移回）
        if (!IsRegionFullyMapped(dstAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapMemory: 目标地址未完全映射 dst=0x{dstAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 验证源地址未映射（防止覆盖/泄漏）
        if (IsRegionMapped(srcAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapMemory: 源地址已映射 src=0x{srcAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        // 验证源和目标范围不重叠
        if (RangesOverlap(srcAddr, size, dstAddr, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapMemory: 源和目标范围重叠 src=0x{srcAddr:X16} dst=0x{dstAddr:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 查找原始 MemoryType（MapMemory 时保存）
        MemoryType restoreType = MemoryType.Normal;
        var mapKey = (ActiveProcess.Info.ProcessId, dstAddr);
        if (_mapMemoryOriginalTypes.TryGetValue(mapKey, out var saved))
        {
            restoreType = saved.OriginalType;
            _mapMemoryOriginalTypes.Remove(mapKey);
        }

        try
        {
            // 逆操作：将物理页从 dst 搬回 src，恢复原始 MemoryType
            _memory.Remap(dstAddr, srcAddr, size, MemoryPermissions.ReadWrite, restoreType);

            // 同步 HVF
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
            {
                hvfEngine.UnmapMemoryFromHvf(dstAddr, size);
                hvfEngine.MapMemoryToHvf(srcAddr, size, MemoryPermissions.ReadWrite);
            }

            Logger.Info(nameof(HorizonSystem),
                $"UnmapMemory: 0x{dstAddr:X16} → 0x{srcAddr:X16} 大小=0x{size:X16} restoreType={restoreType}");
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"UnmapMemory: 内存访问错误 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x35/0x36 MapPhysicalMemory/UnmapPhysicalMemory ────────────────────

    /// <summary>
    /// 实现 SVC 0x35 MapPhysicalMemory
    /// 将物理内存映射到进程虚拟地址空间（RW 权限）
    /// 输入: X0 = 地址, X2 = 大小
    /// 输出: W0 = ResultCode
    /// </summary>
    public SvcResult MapPhysicalMemory(SvcInfo svc)
    {
        ulong address = svc.X0;
        ulong size = svc.X2;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证对齐
        if (!IsPageAligned(address) || !IsPageAligned(size) || size == 0)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapPhysicalMemory: 地址或大小未对齐 addr=0x{address:X16} size=0x{size:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证地址在进程空间内
        if (!IsInProcessAddressSpace(address, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapPhysicalMemory: 地址超出进程空间 addr=0x{address:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证区域未被映射（真实 Horizon 不允许覆盖已有映射）
        if (IsRegionMapped(address, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"MapPhysicalMemory: 地址已映射 addr=0x{address:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        try
        {
            // 映射零填充的 RW 页（模拟物理内存，标记为 Io 类型以便 QueryMemory 区分）
            _memory.MapZero(address, size, MemoryPermissions.ReadWrite, MemoryType.Io);

            // 同步 HVF
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
            {
                hvfEngine.MapMemoryToHvf(address, size, MemoryPermissions.ReadWrite);
            }

            Logger.Info(nameof(HorizonSystem),
                $"MapPhysicalMemory: 0x{address:X16} 大小=0x{size:X16}");
        }
        catch (MemoryAllocationException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"MapPhysicalMemory: 内存分配失败 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.OutOfResource) };
        }

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    /// <summary>
    /// 实现 SVC 0x36 UnmapPhysicalMemory
    /// 取消映射之前通过 MapPhysicalMemory 映射的物理内存
    /// 输入: X0 = 地址, X2 = 大小
    /// 输出: W0 = ResultCode
    /// </summary>
    public SvcResult UnmapPhysicalMemory(SvcInfo svc)
    {
        ulong address = svc.X0;
        ulong size = svc.X2;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 验证对齐
        if (!IsPageAligned(address) || !IsPageAligned(size) || size == 0)
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapPhysicalMemory: 地址或大小未对齐 addr=0x{address:X16} size=0x{size:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 验证地址在进程空间内
        if (!IsInProcessAddressSpace(address, size))
        {
            Logger.Warning(nameof(HorizonSystem),
                $"UnmapPhysicalMemory: 地址超出进程空间 addr=0x{address:X16}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        try
        {
            // 同步 HVF 先取消映射
            if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
            {
                hvfEngine.UnmapMemoryFromHvf(address, size);
            }

            // 取消映射并释放物理页
            _memory.Unmap(address, size);

            Logger.Info(nameof(HorizonSystem),
                $"UnmapPhysicalMemory: 0x{address:X16} 大小=0x{size:X16}");
        }
        catch (MemoryAccessException ex)
        {
            Logger.Warning(nameof(HorizonSystem), $"UnmapPhysicalMemory: 内存访问错误 — {ex.Message}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    // ──────────────────── SVC 0x29 GetInfo ────────────────────

    /// <summary>
    /// 实现 SVC 0x29 GetInfo
    /// 查询系统和进程信息
    /// 输入: X1 = InfoType, X2 = Handle, X3 = InfoSubType
    /// 输出: W0 = ResultCode, X1 = InfoValue
    /// </summary>
    public SvcResult GetInfo(SvcInfo svc)
    {
        var infoType = (InfoType)svc.X1;
        ulong handle = svc.X2;
        ulong subType = svc.X3;

        // 验证句柄：InfoType 0-7,12-18,20-23 需要进程句柄（0xFFFF8000 或真实句柄）
        // InfoType 8-11,19 需要零句柄
        bool requiresProcessHandle = infoType switch
        {
            InfoType.CoreMask or
            InfoType.PriorityMask or
            InfoType.AliasRegionAddress or
            InfoType.AliasRegionSize or
            InfoType.HeapRegionAddress or
            InfoType.HeapRegionSize or
            InfoType.TotalMemorySize or
            InfoType.UsedMemorySize or
            InfoType.AslrRegionAddress or
            InfoType.AslrRegionSize or
            InfoType.StackRegionAddress or
            InfoType.StackRegionSize or
            InfoType.SystemResourceSizeTotal or
            InfoType.SystemResourceSizeUsed or
            InfoType.ProgramId or
            InfoType.UserExceptionContextAddress or
            InfoType.TotalNonSystemMemorySize or
            InfoType.UsedNonSystemMemorySize or
            InfoType.IsApplication => true,
            InfoType.DebuggerAttached or
            InfoType.ResourceLimit or
            InfoType.IdleTickCount or
            InfoType.RandomEntropy or
            InfoType.InitialProcessIdRange => false,
            _ => true
        };

        if (requiresProcessHandle)
        {
            // 必须提供当前进程伪句柄 (0xFFFF8001) 或真实进程句柄
            if (handle != CurrentProcessPseudoHandle)
            {
                if (!IsValidProcessHandle(handle))
                {
                    return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
                }
            }

            if (ActiveProcess == null)
            {
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
            }
        }
        else
        {
            // 需要零句柄（部分 InfoType 也接受伪句柄）
            if (handle != 0 && handle != CurrentProcessPseudoHandle)
            {
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
            }
        }

        // 检查 InfoType 是否在已知范围内
        if (!IsValidInfoType(infoType))
        {
            Logger.Warning(nameof(HorizonSystem), $"GetInfo: 未支持的 InfoType={infoType}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        ulong infoValue = infoType switch
        {
            // ── 进程内存信息 ──
            InfoType.CoreMask => 0xFUL, // 核心 0-3
            InfoType.PriorityMask => 0xFFFFFFFFFFFFFFFFUL, 
            InfoType.AliasRegionAddress => AliasBase,
            InfoType.AliasRegionSize => AliasSize,
            InfoType.HeapRegionAddress => HeapBase,
            InfoType.HeapRegionSize => HeapMaxSize,
            InfoType.TotalMemorySize => CalculateTotalMemory(),
            InfoType.UsedMemorySize => CalculateUsedMemory(),
            InfoType.AslrRegionAddress => AslrBase,
            InfoType.AslrRegionSize => AslrSize,
            InfoType.StackRegionAddress => StackBase,
            InfoType.StackRegionSize => ActiveProcess!.Info.MainStackSize,

            // ── 系统资源 ──
            InfoType.SystemResourceSizeTotal => 0x200000UL, // 2MB 系统资源
            InfoType.SystemResourceSizeUsed => 0x1000UL,   // 4KB 已用
            InfoType.ProgramId => ActiveProcess!.Info.TitleId,

            // ── 调试信息 ──
            InfoType.DebuggerAttached => 0UL, // 无调试器附加

            // ── 资源限制 ──
            InfoType.ResourceLimit => (ulong)(ActiveProcess?.HandleTable.CreateHandle(new KResourceLimit()) ?? 0),

            // ── 系统统计 ──
            InfoType.IdleTickCount => 0UL, // 简化：无空闲计数

            // ── 随机熵 ──
            InfoType.RandomEntropy => GenerateRandomEntropy(subType),

            // ── 进程 ID 范围 ──
            InfoType.InitialProcessIdRange => subType == 0 ? 100UL : 200UL, // PID 范围 [100, 200)

            // ── 异常上下文 ──
            InfoType.UserExceptionContextAddress => 0UL, // 无异常上下文

            // ── 非系统内存 ──
            InfoType.TotalNonSystemMemorySize => 0x20000000UL, // 512MB
            InfoType.UsedNonSystemMemorySize => 0x1000000UL,   // 16MB

            // ── 应用标识 ──
            InfoType.IsApplication => ActiveProcess!.Info.Category == ProcessCategory.Application ? 1UL : 0UL,

            // 不会到达（上方已验证 IsValidInfoType），但编译器需要
            _ => 0UL
        };

        Logger.Info(nameof(HorizonSystem),
            $"GetInfo: type={infoType} ({(int)infoType}) handle=0x{handle:X8} sub={subType} → 0x{infoValue:X16}");

        return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = infoValue };
    }

    /// <summary>计算进程总内存大小</summary>
    private static ulong CalculateTotalMemory()
    {
        // 模拟 4GB 物理内存
        return 0x100000000UL;
    }

    /// <summary>计算进程已用内存大小</summary>
    private ulong CalculateUsedMemory()
    {
        if (ActiveProcess == null) return 0;
        // 模拟已用内存，固定留出 256MB 的空闲给 libnx 支配
        return CalculateTotalMemory() - 0x10000000UL;
    }

    /// <summary>生成随机熵值</summary>
    private ulong GenerateRandomEntropy(ulong subType)
    {
        if (subType > 3)
        {
            Logger.Warning(nameof(HorizonSystem), $"GetInfo: RandomEntropy subType={subType} 超出范围 0-3");
            return 0;
        }
        return (ulong)_rng.NextInt64();
    }

    /// <summary>检查 InfoType 是否为已知类型</summary>
    private static bool IsValidInfoType(InfoType infoType)
    {
        return infoType >= InfoType.CoreMask && infoType <= InfoType.IsApplication;
    }

    /// <summary>检查句柄是否为有效进程句柄</summary>
    /// <remarks>当前简化实现：只要句柄在表中且类型正确（或暂时允许任何有效句柄）即认为有效</remarks>
    private bool IsValidProcessHandle(ulong handle)
    {
        if (handle == 0) return false;
        // 在目前的单进程 HLE 模拟中，我们允许使用主线程句柄或进程伪句柄。
        // TODO: 严格校验应检查对象是否为 KProcess
        return ActiveProcess?.HandleTable.IsValid((int)handle) ?? false;
    }

    /// <summary>计算非系统内存（防止无符号下溢）</summary>
    private static ulong CalculateNonSystemMemory(ulong total, ulong systemSize)
    {
        return total > systemSize ? total - systemSize : 0;
    }

    /// <summary>检查地址或大小是否 4KB 对齐 (Horizon OS 标准)</summary>
    private static bool IsPageAligned(ulong value) => (value & 0xFFFUL) == 0;

 /// <summary>
 /// 检查地址范围是否在进程地址空间内
 /// 范围: TLS 基地址 (0x0100_0000) ~ Heap + 256MB (0x2800_0000)
 /// 包含所有区域: TLS (0x0100_0000), 栈, NRO/ASLR (0x0800_0000), Alias, Heap
 /// </summary>
 private static bool IsInProcessAddressSpace(ulong address, ulong size)
 {
 // TLS 区域起点
 const ulong tlsBase = 0x0000_0100_0000UL;
 // Heap 区域终点 (HeapBase + HeapMaxSize = 0x2000_0000 + 0x8000_0000 = 0x2800_0000)
 const ulong processSpaceEnd = HeapBase + HeapMaxSize; // 0x2800_0000

 return address >= tlsBase && (address + size) <= processSpaceEnd;
 }

    /// <summary>检查虚拟地址范围内是否有任何页已映射</summary>
    private bool IsRegionMapped(ulong vaddr, ulong size)
    {
        var alignedAddr = vaddr & ~0xFFFUL;
        var endAddr = (vaddr + size + 0xFFFUL) & ~0xFFFUL;
        for (ulong addr = alignedAddr; addr < endAddr; addr += 0x1000)
        {
            if (_memory.IsMapped(addr)) return true;
        }
        return false;
    }

    /// <summary>检查虚拟地址范围内是否所有页都已映射</summary>
    private bool IsRegionFullyMapped(ulong vaddr, ulong size)
    {
        var alignedAddr = vaddr & ~0xFFFUL;
        var endAddr = (vaddr + size + 0xFFFUL) & ~0xFFFUL;
        for (ulong addr = alignedAddr; addr < endAddr; addr += 0x1000)
        {
            if (!_memory.IsMapped(addr)) return false;
        }
        return true;
    }

    /// <summary>检查两个地址范围是否重叠</summary>
    private static bool RangesOverlap(ulong addr1, ulong size1, ulong addr2, ulong size2)
    {
        return addr1 < addr2 + size2 && addr2 < addr1 + size1;
    }

    private static ulong AlignPage4K(ulong size) => (size + 0xFFFUL) & ~0xFFFUL; // 4KB 对齐 (Horizon OS 标准)

    /// <summary>
    /// 清理进程级同步状态（互斥锁和 futex 信号）
    /// 进程退出时调用，防止残留数据影响后续进程
    /// </summary>
    private void CleanupProcessSyncState(ulong processId)
    {
        // 当前单进程模型：清空所有同步状态
        // 多进程模型下需要按 PID 过滤
        _processWideKeySignals.Clear();
        // 移除该进程的 MapMemory 原始类型记录
        foreach (var key in _mapMemoryOriginalTypes.Keys.ToList())
        {
            if (key.ProcessId == processId)
                _mapMemoryOriginalTypes.Remove(key);
        }
        Logger.Debug(nameof(HorizonSystem), $"清理进程 PID={processId} 的同步状态");
    }

    /// <summary>获取所有进程</summary>
    public IReadOnlyCollection<HorizonProcess> GetAllProcesses() => _processes.Values;

    public void Dispose()
    {
        foreach (var process in _processes.Values)
        {
            process.Dispose();
        }
        _processes.Clear();
    }
}
