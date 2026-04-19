using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Memory;

namespace SwitchNro.Cpu.Hypervisor;

/// <summary>
/// Hypervisor.framework 执行引擎
/// 利用 macOS HVF 将 NRO 的 ARM64 代码直接映射到虚拟 CPU 执行
/// </summary>
public sealed class HvfExecutionEngine : IExecutionEngine
{
    private ulong _vcpu;
    private IntPtr _exitPtr; // ARM64: hv_vcpu_create 返回的退出结构体指针
    private bool _isRunning;
    private bool _exitRequested;
    private SvcInfo _lastSvc;
    private readonly VirtualMemoryManager _memory;

    /// <summary>Apple Silicon 页大小 = 16KB (0x4000)</summary>
    private const ulong ArmPageSize = 0x4000;
    private const ulong ArmPageMask = ArmPageSize - 1;

    /// <summary>记录已映射块的权限，用于处理权限升级</summary>
    private readonly Dictionary<ulong, ulong> _mappedBlocks = new();

    // ──────────────────── vCPU 超时强制退出机制 ────────────────────

    /// <summary>每次 hv_vcpu_run() 的超时时间（毫秒）。0 = 无超时。</summary>
    private int _vcpuTimeoutMs = 100;

    /// <summary>超时定时器（ThreadPool 线程上触发 hv_vcpus_exit）</summary>
    private System.Threading.Timer? _vcpuTimer;

    /// <summary>标记当前 hv_vcpu_run() 是否因超时被强制退出</summary>
    private bool _timeoutExpired;

    /// <summary>连续超时次数（两次 SVC 之间的超时次数）</summary>
    private int _consecutiveTimeouts;

    /// <summary>连续超时上限，超过后返回 ExecutionResult.Timeout</summary>
    private int _maxConsecutiveTimeouts = 50;

    // ──────────────────── 诊断统计 ────────────────────

    /// <summary>诊断计数器（前 N 次退出打印详细信息）</summary>
    private int _diagCounter;

    /// <summary>诊断详细信息打印上限</summary>
    private const int DiagLimit = 200;

    /// <summary>vCPU 退出总次数</summary>
    private int _totalExits;

    /// <summary>退出原因统计 (Reason → count)</summary>
    private readonly Dictionary<uint, int> _exitReasonCounts = new();

    /// <summary>异常类型统计 (EC → count)</summary>
    private readonly Dictionary<uint, int> _exceptionClassCounts = new();

    public ExecutionMode Mode => ExecutionMode.Hypervisor;
    public bool IsRunning => _isRunning;

    /// <summary>每次 hv_vcpu_run() 的超时时间（毫秒）。0 = 无超时。</summary>
    public int VcpuTimeoutMs
    {
        get => _vcpuTimeoutMs;
        set => _vcpuTimeoutMs = value;
    }

    /// <summary>连续超时上限。超过后停止执行，返回 ExecutionResult.Timeout。</summary>
    public int MaxConsecutiveTimeouts
    {
        get => _maxConsecutiveTimeouts;
        set => _maxConsecutiveTimeouts = value;
    }

    /// <summary>vCPU 退出总次数</summary>
    public int TotalExits => _totalExits;

    /// <summary>退出原因统计 (Reason → count)</summary>
    public IReadOnlyDictionary<uint, int> ExitReasonCounts => _exitReasonCounts;

    /// <summary>异常类型统计 (EC → count)</summary>
    public IReadOnlyDictionary<uint, int> ExceptionClassCounts => _exceptionClassCounts;

    /// <summary>异常向量表（Trampoline）在 VMM 物理内存中的基地址，设为 0 以利用系统默认 VBAR_EL1</summary>
    private const ulong VbarTrampolineBase = 0x0000_0000_0000_0000UL;

    public HvfExecutionEngine(VirtualMemoryManager memory)
    {
        _memory = memory;
        Initialize();
        SetupExceptionTrampoline();
    }

    private static void SetupExceptionTrampoline()
    {
        // 我们不显式设置 VBAR_EL1，让它保持为默认的 0。
        // 当发生 SVC 等异常时，vCPU 会跳到 0x400 处执行。
        // 由于 0x400 处的内容是 0（未定义指令），这会产生一个 EC=0x20 的异常直接退回到 Host！
        // Host 可以通过判断 PC 是否为 0x400，并读取 ESR_EL1 来判断是否为 SVC。
    }

    private static bool _globalVmCreated;

    private void Initialize()
    {
        // 检查全局虚拟机是否已创建 (HVF 在进程内通常只允许一个实例)
        if (!_globalVmCreated)
        {
            int ret = NativeHvf.hv_vm_create(IntPtr.Zero);
            if (ret != 0 && ret != -85377017) // -85377017 是 HV_BUSY，表示已存在
            {
                Logger.Error(nameof(HvfExecutionEngine), $"hv_vm_create 失败: {ret}");
                throw new InvalidOperationException($"Hypervisor 初始化失败: hv_vm_create 返回 {ret}");
            }
            _globalVmCreated = true;
        }

        // 创建虚拟 CPU
        IntPtr exitPtr;
        int vcpuRet = NativeHvf.hv_vcpu_create(out _vcpu, out exitPtr, 0);
        _exitPtr = exitPtr;
        
        if (vcpuRet != 0)
        {
            Logger.Error(nameof(HvfExecutionEngine), $"hv_vcpu_create 失败: {vcpuRet}");
            throw new InvalidOperationException($"vCPU 创建失败: hv_vcpu_create 返回 {vcpuRet}");
        }

        if (_exitPtr == IntPtr.Zero)
        {
            Logger.Error(nameof(HvfExecutionEngine), "hv_vcpu_create 返回的 exit 指针为 NULL");
            throw new InvalidOperationException("hv_vcpu_create 未返回有效的退出结构体指针");
        }

        Logger.Info(nameof(HvfExecutionEngine), "Hypervisor 执行引擎初始化完成");
    }

    /// <summary>取消映射 HVF 物理地址空间中的内存区域（16KB 对齐）</summary>
    public void UnmapMemoryFromHvf(ulong gpa, ulong size)
    {
        // 对齐到 16KB 边界
        ulong alignedGpa = gpa & ~ArmPageMask;
        ulong alignedEnd = (gpa + size + ArmPageMask) & ~ArmPageMask;
        ulong alignedSize = alignedEnd - alignedGpa;

        // 逐块取消映射
        for (ulong addr = alignedGpa; addr < alignedEnd; addr += ArmPageSize)
        {
            _mappedBlocks.Remove(addr);
        }

        int ret = NativeHvf.hv_vm_unmap(alignedGpa, alignedSize);
        if (ret != 0)
        {
            Logger.Error(nameof(HvfExecutionEngine),
                $"hv_vm_unmap 失败: GPA=0x{alignedGpa:X16} Size=0x{alignedSize:X16} ret={ret}");
        }
    }

    /// <summary>
    /// 将虚拟内存页映射到 HVF 物理地址空间（直接映射 16KB 物理块）
    /// 
    /// VMM 的物理内存以 16KB 块分配（mmap），每个块包含 4 个 4KB 子页。
    /// 此方法直接将 VMM 的 16KB 块映射到 HVF，无需数据复制。
    /// 
    /// 关键优势：
    /// - VMM 和 HVF 共享同一块物理内存
    /// - Guest 写入立即可见（SVC 处理器通过 VMM 读取的数据始终是最新的）
    /// - 无需在 SVC 处理后同步 VMM/HVF 数据
    /// </summary>
    public void MapMemoryToHvf(ulong gpa, ulong size, MemoryPermissions perms)
    {
        ulong hvfPerms = 0;
        if ((perms & MemoryPermissions.Read) != 0) hvfPerms |= NativeHvf.HV_VM_MAP_READ;
        if ((perms & MemoryPermissions.Write) != 0) hvfPerms |= NativeHvf.HV_VM_MAP_WRITE;
        if ((perms & MemoryPermissions.Execute) != 0) hvfPerms |= NativeHvf.HV_VM_MAP_EXECUTE;

        // 对齐到 16KB 边界
        ulong startAddr = gpa & ~ArmPageMask;
        ulong endAddr = (gpa + size + ArmPageMask) & ~ArmPageMask;

        int mappedBlocks = 0;
        for (ulong blockAddr = startAddr; blockAddr < endAddr; blockAddr += ArmPageSize)
        {
            // 如果已映射，检查是否需要权限升级
            if (_mappedBlocks.TryGetValue(blockAddr, out var currentPerms))
            {
                // 如果当前权限已经包含所需的权限，则跳过
                if ((currentPerms & hvfPerms) == hvfPerms)
                {
                    continue;
                }
                
                // 需要权限升级 (比如 16KB 页面一半是 rodata，一半是 data，需要从 R 升级到 RW)
                Logger.Debug(nameof(HvfExecutionEngine),
                    $"hv_vm_map: 升级权限 GPA=0x{blockAddr:X16} 0x{currentPerms:X} -> 0x{currentPerms | hvfPerms:X}");
                
                // 先取消映射
                _ = NativeHvf.hv_vm_unmap(blockAddr, ArmPageSize);
                hvfPerms |= currentPerms; // 保留原有权限
            }

            // 获取 VMM 中该 16KB 块的 host 基地址
            var (hostBase, blockGpa) = _memory.GetHvfBlockInfo(blockAddr);
            if (hostBase == IntPtr.Zero)
            {
                // 该块在 VMM 中未分配，跳过
                //（可能是 16KB 块中只有部分 4KB 子页被使用）
                Logger.Debug(nameof(HvfExecutionEngine),
                    $"hv_vm_map: VMM 块未分配 GPA=0x{blockAddr:X16}，跳过");
                continue;
            }

            // 直接映射：VMM 的 16KB mmap 块天然 16KB 对齐
            int ret = NativeHvf.hv_vm_map(hostBase, blockGpa, ArmPageSize, hvfPerms);
            if (ret != 0)
            {
                Logger.Error(nameof(HvfExecutionEngine),
                    $"hv_vm_map 失败: GPA=0x{blockGpa:X16} Size=0x{ArmPageSize:X} " +
                    $"HostBase=0x{(long)hostBase:X16} Perms=0x{hvfPerms:X} ret={ret}");
                continue;
            }

            _mappedBlocks[blockAddr] = hvfPerms;
            mappedBlocks++;
        }

        if (mappedBlocks > 0)
        {
            Logger.Debug(nameof(HvfExecutionEngine),
                $"hv_vm_map: GPA=0x{startAddr:X16}-0x{endAddr:X16} " +
                $"Perms=[{perms}] mapped={mappedBlocks} blocks");
        }
    }

    public ExecutionResult Execute(ulong entryPoint)
    {
        SetPC(entryPoint);
        SetPstate(0x0); // 设置为 EL0h 模式

        return RunLoop();
    }

    public ExecutionResult RunNext()
    {
        return RunLoop();
    }

    private ExecutionResult RunLoop()
    {
        _isRunning = true;
        _exitRequested = false;
        _consecutiveTimeouts = 0;

        try
        {
            while (!_exitRequested)
            {
                // 1. PC 防御性检查 (MVP 调试阶段)
                if (GetPC() == 0)
                {
                    Logger.Error(nameof(HvfExecutionEngine), "致命错误: 检测到 PC=0，Guest 程序执行异常！");
                    return ExecutionResult.NormalExit;
                }

                // 启动超时定时器：在 _vcpuTimeoutMs 毫秒后强制退出 vCPU
                _timeoutExpired = false;
                if (_vcpuTimeoutMs > 0)
                {
                    _vcpuTimer = new System.Threading.Timer(
                        _ => OnVcpuTimeout(), null, _vcpuTimeoutMs, System.Threading.Timeout.Infinite);
                }

                int ret = NativeHvf.hv_vcpu_run(_vcpu);

                // 停止超时定时器
                _vcpuTimer?.Dispose();
                _vcpuTimer = null;

                if (ret != 0)
                {
                    Logger.Error(nameof(HvfExecutionEngine), $"hv_vcpu_run 失败: {ret}");
                    return ExecutionResult.NormalExit;
                }

                // ARM64: 从 hv_vcpu_create 返回的 exit 指针读取退出结构体
                var exitInfo = Marshal.PtrToStructure<NativeHvf.HvVcpuExit>(_exitPtr);

                // 统计退出原因
                _totalExits++;
                if (!_exitReasonCounts.ContainsKey(exitInfo.Reason))
                    _exitReasonCounts[exitInfo.Reason] = 0;
                _exitReasonCounts[exitInfo.Reason]++;

                // 诊断：打印非 VTIMER 退出 (且仅限前 DiagLimit 次，防止溢出)
                if (exitInfo.Reason != NativeHvf.HV_EXIT_REASON_VTIMER_ACTIVATED && _diagCounter < DiagLimit)
                {
                    var diagPC = GetPC();
                    Console.WriteLine($"  [HVF #{_diagCounter}] 退出原因={exitInfo.Reason} PC=0x{diagPC:X16} EC=0x{exitInfo.ExceptionClass:X2} ISS=0x{exitInfo.Iss:X7}");
                    _diagCounter++;
                }

                switch (exitInfo.Reason)
                {
                    case NativeHvf.HV_EXIT_REASON_EXCEPTION:
                    {
                        // 强制诊断：捕获异常现场
                        ulong pc = GetPC();
                        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_ELR_EL1, out var elr);
                        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_FAR_EL1, out var far);
                        Console.WriteLine($"  [!!!] 捕获到异常! PC=0x{pc:X16} EC=0x{exitInfo.ExceptionClass:X2} ELR_EL1=0x{elr:X16} FAR_EL1=0x{far:X16}");
                        
                        _consecutiveTimeouts = 0;
                        var result = HandleException(exitInfo);
                        return result;
                    }

                    case NativeHvf.HV_EXIT_REASON_CANCELED:
                    {
                        // 区分超时强制退出 vs. 调试暂停
                        if (_timeoutExpired)
                        {
                            _timeoutExpired = false;
                            _consecutiveTimeouts++;

                            if (_consecutiveTimeouts >= _maxConsecutiveTimeouts)
                            {
                                Console.WriteLine($"  ⚠️ 连续 {_consecutiveTimeouts} 次超时，可能执行卡死 → Timeout");
                                return ExecutionResult.Timeout;
                            }
                            continue;
                        }
                        return ExecutionResult.DebugPause;
                    }

                    case NativeHvf.HV_EXIT_REASON_VTIMER_ACTIVATED:
                        // 定时器触发，直接继续
                        continue;

                    default:
                        Logger.Warning(nameof(HvfExecutionEngine),
                            $"未知退出原因: {exitInfo.Reason}");
                        return ExecutionResult.NormalExit;
                }
            }

            return ExecutionResult.NormalExit;
        }
        finally
        {
            _vcpuTimer?.Dispose();
            _vcpuTimer = null;
            _isRunning = false;
        }
    }

    /// <summary>
    /// vCPU 超时回调：在 ThreadPool 线程上调用 hv_vcpus_exit()
    /// 使 hv_vcpu_run() 以 HV_EXIT_REASON_CANCELED 原因退出
    /// </summary>
    private unsafe void OnVcpuTimeout()
    {
        _timeoutExpired = true;
        ulong vcpu = _vcpu;
        _ = NativeHvf.hv_vcpus_exit(&vcpu, 1);
    }

    /// <summary>打印 vCPU 退出统计摘要</summary>
    public void PrintExitStatistics()
    {
        Console.WriteLine($"  vCPU 退出统计: 总计 {_totalExits} 次");
        foreach (var (reason, count) in _exitReasonCounts)
        {
            string name = reason switch
            {
                NativeHvf.HV_EXIT_REASON_EXCEPTION => "EXCEPTION",
                NativeHvf.HV_EXIT_REASON_VTIMER_ACTIVATED => "VTIMER",
                NativeHvf.HV_EXIT_REASON_CANCELED => "CANCELED",
                _ => $"UNKNOWN({reason})"
            };
            Console.WriteLine($"    {name}: {count}");
        }
        foreach (var (ec, count) in _exceptionClassCounts)
        {
            string name = ec switch
            {
                NativeHvf.EC_SVC64 => "SVC64",
                NativeHvf.EC_BKPT => "BKPT",
                NativeHvf.EC_DABORT => "DABORT",
                NativeHvf.EC_UNKNOWN => "UNKNOWN",
                _ => $"EC=0x{ec:X2}"
            };
            Console.WriteLine($"    异常 {name}: {count}");
        }
    }

    private ExecutionResult HandleException(NativeHvf.HvVcpuExit exit)
    {
        // 统计异常类型
        if (!_exceptionClassCounts.ContainsKey(exit.ExceptionClass))
            _exceptionClassCounts[exit.ExceptionClass] = 0;
        _exceptionClassCounts[exit.ExceptionClass]++;

        switch (exit.ExceptionClass)
        {
            case NativeHvf.EC_SVC64:
                // SVC 系统调用
                // ARM64: Syndrome 在退出结构体中，低 16 位为 SVC 编号
                var svcNumber = exit.Iss & 0xFFFF;
                _lastSvc = new SvcInfo
                {
                    SvcNumber = svcNumber,
                    X0 = GetRegister(0),
                    X1 = GetRegister(1),
                    X2 = GetRegister(2),
                    X3 = GetRegister(3),
                    X4 = GetRegister(4),
                    X5 = GetRegister(5),
                    X6 = GetRegister(6),
                    X7 = GetRegister(7),
                    PC = GetPC(),
                    SP = GetSP(),
                };
                Logger.Debug(nameof(HvfExecutionEngine), $"SVC 拦截: {_lastSvc}");
                return ExecutionResult.SVC;

            case NativeHvf.EC_UNKNOWN: // 未知或非法指令
            case 0x20: // Instruction Abort
                // 如果是从我们的异常 Trampoline 退出的（PC 在 VBAR 范围内）
                ulong pc = GetPC();
                if ((pc & ~0xFFFUL) == VbarTrampolineBase)
                {
                    // 这是从 Guest 陷入的异常！读取真正的异常原因 (ESR_EL1)
                    _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_ESR_EL1, out var esr);
                    _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_ELR_EL1, out var elr);
                    _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_SPSR_EL1, out var spsr);

                    uint realEc = (uint)((esr >> 26) & 0x3F);
                    uint realIss = (uint)(esr & 0x1FFFFFF);

                    if (realEc == NativeHvf.EC_SVC64)
                    {
                        var svcNum = realIss & 0xFFFF;
                        _lastSvc = new SvcInfo
                        {
                            SvcNumber = svcNum,
                            X0 = GetRegister(0),
                            X1 = GetRegister(1),
                            X2 = GetRegister(2),
                            X3 = GetRegister(3),
                            X4 = GetRegister(4),
                            X5 = GetRegister(5),
                            X6 = GetRegister(6),
                            X7 = GetRegister(7),
                            PC = elr, // 真实的调用 SVC 时的 PC
                            SP = GetSP(),
                        };
                        
                        // 从 Trampoline 恢复状态到调用 SVC 后
                        SetPC(elr);     // SVC 指令的下一条指令
                        SetPstate(spsr); // 恢复被修改的 PSTATE
                        
                        Logger.Debug(nameof(HvfExecutionEngine), $"SVC 拦截 (Trampoline): {_lastSvc}");
                        return ExecutionResult.SVC;
                    }
                    else
                    {
                        Logger.Error(nameof(HvfExecutionEngine), $"Trampoline 捕获未处理的异常: EC=0x{realEc:X2} ISS=0x{realIss:X7} ELR=0x{elr:X16}");
                        return ExecutionResult.UndefinedInstruction;
                    }
                }
                
                Logger.Error(nameof(HvfExecutionEngine), $"未定义指令: PC=0x{pc:X16}");
                return ExecutionResult.UndefinedInstruction;

            case NativeHvf.EC_BKPT:
                Logger.Info(nameof(HvfExecutionEngine), $"断点触发: PC=0x{GetPC():X16}");
                return ExecutionResult.Breakpoint;

            case NativeHvf.EC_DABORT:
                Logger.Warning(nameof(HvfExecutionEngine),
                    $"数据异常: VA=0x{exit.FaultVirtualAddress:X16} PA=0x{exit.FaultPhysicalAddress:X16}");
                return ExecutionResult.MemoryFault;

            default:
                Logger.Error(nameof(HvfExecutionEngine),
                    $"未处理异常: EC=0x{exit.ExceptionClass:X2} Syndrome=0x{exit.Syndrome:X16}");
                return ExecutionResult.UndefinedInstruction;
        }
    }

    public unsafe void Pause()
    {
        ulong vcpu = _vcpu;
        _ = NativeHvf.hv_vcpus_exit(&vcpu, 1);
    }
    public void RequestExit() => _exitRequested = true;

    public ulong GetRegister(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 31);
        _ = NativeHvf.hv_vcpu_get_reg(_vcpu, (uint)index, out var value);
        return value;
    }

    public void SetRegister(int index, ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 31);
        _ = NativeHvf.hv_vcpu_set_reg(_vcpu, (uint)index, value);
    }

    public ulong GetPC()
    {
        // PC 是通用寄存器 (HV_REG_PC = 31)，不是系统寄存器
        _ = NativeHvf.hv_vcpu_get_reg(_vcpu, NativeHvf.REG_PC, out var value);
        return value;
    }

    public void SetPC(ulong value)
    {
        // PC 是通用寄存器 (HV_REG_PC = 31)，不是系统寄存器
        int ret = NativeHvf.hv_vcpu_set_reg(_vcpu, NativeHvf.REG_PC, value);
        if (ret != 0)
        {
            Console.WriteLine($"  [!!!] ERROR: SetPC(0x{value:X16}) 失败! ret={ret}");
        }
        else
        {
            int verifyRet = NativeHvf.hv_vcpu_get_reg(_vcpu, NativeHvf.REG_PC, out ulong verify);
            if (verifyRet == 0)
                Console.WriteLine($"  [HVF 校验] SetPC(0x{value:X16}) -> 回读PC=0x{verify:X16}");
            else
                Console.WriteLine($"  [!!!] ERROR: GetPC 验证失败! ret={verifyRet}");
        }
    }

    public ulong GetSP()
    {
        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_SP_EL0, out var value);
        return value;
    }

    public void SetSP(ulong value)
    {
        _ = NativeHvf.hv_vcpu_set_sys_reg(_vcpu, NativeHvf.SYS_REG_SP_EL0, value);
    }

    public SvcInfo GetLastSvcInfo() => _lastSvc;

    public void SetSvcResult(ulong returnValue)
    {
        SetRegister(0, returnValue);
    }

    public void SetSvcResult(ulong returnValue0, ulong returnValue1)
    {
        SetRegister(0, returnValue0);
        SetRegister(1, returnValue1);
    }

    public void SetSvcResult(ulong returnValue0, ulong returnValue1, ulong returnValue2)
    {
        SetRegister(0, returnValue0);
        SetRegister(1, returnValue1);
        SetRegister(2, returnValue2);
    }

    public ulong GetPstate()
    {
        // CPSR/PSTATE 是通用寄存器 (HV_REG_CPSR = 34)，不是系统寄存器
        _ = NativeHvf.hv_vcpu_get_reg(_vcpu, NativeHvf.REG_CPSR, out var value);
        return value;
    }

    public void SetPstate(ulong value)
    {
        // CPSR/PSTATE 是通用寄存器 (HV_REG_CPSR = 34)，不是系统寄存器
        _ = NativeHvf.hv_vcpu_set_reg(_vcpu, NativeHvf.REG_CPSR, value);
    }

    public ulong GetTpidrEl0()
    {
        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_TPIDR_EL0, out var value);
        return value;
    }

    public void SetTpidrEl0(ulong value)
    {
        _ = NativeHvf.hv_vcpu_set_sys_reg(_vcpu, NativeHvf.SYS_REG_TPIDR_EL0, value);
    }

    public ulong GetTpidrroEl0()
    {
        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_TPIDRRO_EL0, out var value);
        return value;
    }

    public void SetTpidrroEl0(ulong value)
    {
        _ = NativeHvf.hv_vcpu_set_sys_reg(_vcpu, NativeHvf.SYS_REG_TPIDRRO_EL0, value);
    }

    public void Dispose()
    {
        _vcpuTimer?.Dispose();
        _vcpuTimer = null;

        if (_vcpu != 0)
        {
            // 确保 vCPU 被彻底销毁，避免下次加载报 HV_DENIED (-85377018)
            int ret = NativeHvf.hv_vcpu_destroy(_vcpu);
            if (ret != 0)
            {
                Logger.Warning(nameof(HvfExecutionEngine), $"hv_vcpu_destroy 失败: {ret}");
            }
            _vcpu = 0;
        }

        // 记录清理
        _mappedBlocks.Clear();
        _exitReasonCounts.Clear();
        _exceptionClassCounts.Clear();
        Logger.Info(nameof(HvfExecutionEngine), "Hypervisor 执行引擎 vCPU 已释放");
    }
}
