using System;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.Cpu.Hypervisor;
using SwitchNro.Memory;
using SwitchNro.NroLoader;

namespace SwitchNro.Horizon;

/// <summary>
/// Horizon 进程
/// 封装一个 NRO 模块及其对应的执行引擎实例
/// </summary>
public sealed class HorizonProcess : IDisposable
{
    /// <summary>进程信息</summary>
    public ProcessInfo Info { get; }

    /// <summary>执行引擎</summary>
    public IExecutionEngine Engine { get; }

    /// <summary>已加载的 NRO 模块</summary>
    public NroModule NroModule { get; }

    /// <summary>进程状态</summary>
    public ProcessState State { get; set; } = ProcessState.Created;

    /// <summary>进程内核句柄表</summary>
    public HandleTable HandleTable { get; } = new();

    /// <summary>TLS 区域基地址（IPC 缓冲区在 TLS + 0x100）</summary>
    public ulong TlsAddress { get; set; }

    /// <summary>堆内存基地址（SVC 0x01 SetHeapSize 返回）</summary>
    public ulong HeapAddress { get; set; }

    /// <summary>当前堆内存大小（字节）</summary>
    public ulong HeapSize { get; set; }

    /// <summary>
    /// 同步取消标志（SVC 0x0E CancelSynchronization 设置）
    /// 当此标志为 true 时，WaitSynchronization / ReplyAndReceive 立即返回 WaitSyncCancelled
    /// 检查后自动清除
    /// </summary>
    public bool SyncCancelRequested { get; set; }

    /// <summary>
    /// 主线程句柄（通过 Homebrew ABI loader_config 传递给 NRO）
    /// 在 StartProcess 中创建 KThread 并注册到 HandleTable 后赋值
    /// </summary>
    public int MainThreadHandle { get; set; }

    // ──────────────────── 线程管理 ────────────────────

    /// <summary>下一个可用的线程 ID</summary>
    private ulong _nextThreadId;

    /// <summary>当前进程中已创建的线程数量（含主线程）</summary>
    private int _threadCount;

    /// <summary>最大允许创建的线程数（防止资源耗尽）</summary>
    private const int MaxThreadCount = 64;

    /// <summary>下一个可用的 TLS slot 索引（0 = 主线程已占用）</summary>
    private int _nextTlsSlot;

    /// <summary>
    /// 分配下一个线程 ID
    /// </summary>
    public ulong AllocateThreadId() => _nextThreadId++;

    /// <summary>
    /// 尝试增加线程计数，返回是否成功
    /// </summary>
    public bool TryAllocateThreadSlot()
    {
        if (_threadCount >= MaxThreadCount) return false;
        _threadCount++;
        return true;
    }

    /// <summary>
    /// 分配下一个 TLS slot 并返回其基地址
    /// TLS 区域起始于 0x0100_0000，每个线程 0x200 字节
    /// 主线程占用 slot 0，新线程从 slot 1 开始
    /// </summary>
    public ulong AllocateTlsSlot(ulong tlsRegionBase)
    {
        int slot = _nextTlsSlot++;
        return tlsRegionBase + (ulong)slot * HorizonSystem.TlsSize;
    }

    /// <summary>
    /// 减少线程计数（线程终止时调用）
    /// </summary>
    public void ReleaseThreadSlot() => _threadCount--;

    private readonly SvcDispatcher _svcDispatcher;

    internal HorizonProcess(
        ProcessInfo info,
        VirtualMemoryManager memory,
        SvcDispatcher svcDispatcher,
        NroModule nroModule,
        IExecutionEngine? engine = null)
    {
        Info = info;
        _svcDispatcher = svcDispatcher;
        NroModule = nroModule;

        // 初始化线程管理：主线程将占用 slot 0
        _nextThreadId = info.ProcessId + 1; // 主线程 ID = ProcessId，下一个从此开始
        _threadCount = 0; // StartProcess 时会 +1
        _nextTlsSlot = 0; // StartProcess 时分配 slot 0

        // 创建执行引擎（支持依赖注入用于测试）
        Engine = engine ?? new HvfExecutionEngine(memory);

        // 将 NRO 内存映射到 HVF（仅对 HvfExecutionEngine 有效）
        if (Engine is HvfExecutionEngine hvfEngine)
        {
            hvfEngine.MapMemoryToHvf(
                nroModule.TextSegment.Address,
                AlignPage(nroModule.Header.TextSize),
                MemoryPermissions.ReadExecute);
            hvfEngine.MapMemoryToHvf(
                nroModule.RodataSegment.Address,
                AlignPage(nroModule.Header.RodataSize),
                MemoryPermissions.Read);
            hvfEngine.MapMemoryToHvf(
                nroModule.DataSegment.Address,
                AlignPage(nroModule.Header.DataSize),
                MemoryPermissions.ReadWrite);
            // BSS 段也需要同步到 HVF，否则 guest 访问未初始化的全局变量会触发异常
            if (nroModule.Header.BssSize > 0)
                hvfEngine.MapMemoryToHvf(
                    nroModule.BssSegment.Address,
                    AlignPage(nroModule.Header.BssSize),
                    MemoryPermissions.ReadWrite);
        }
    }

    private static ulong AlignPage(uint size) => (size + 0xFFFul) & ~0xFFFul; // 4KB 对齐 (Horizon OS 标准)

    public void Dispose()
    {
        Engine.Dispose();
        Logger.Info(nameof(HorizonProcess), $"进程已释放: PID={Info.ProcessId}");
    }
}
