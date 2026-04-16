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

    private readonly SvcDispatcher _svcDispatcher;

    internal HorizonProcess(
        ProcessInfo info,
        VirtualMemoryManager memory,
        SvcDispatcher svcDispatcher,
        NroModule nroModule)
    {
        Info = info;
        _svcDispatcher = svcDispatcher;
        NroModule = nroModule;

        // 创建 Hypervisor 执行引擎
        Engine = new HvfExecutionEngine(memory);

        // 将 NRO 内存映射到 HVF
        var hvfEngine = Engine as HvfExecutionEngine;
        hvfEngine?.MapMemoryToHvf(
            nroModule.TextSegment.Address,
            AlignPage(nroModule.Header.TextSize),
            MemoryPermissions.ReadExecute);
        hvfEngine?.MapMemoryToHvf(
            nroModule.RodataSegment.Address,
            AlignPage(nroModule.Header.RodataSize),
            MemoryPermissions.Read);
        hvfEngine?.MapMemoryToHvf(
            nroModule.DataSegment.Address,
            AlignPage(nroModule.Header.DataSize),
            MemoryPermissions.ReadWrite);
    }

    private static ulong AlignPage(uint size) => (size + 0xFFFul) & ~0xFFFul;

    public void Dispose()
    {
        Engine.Dispose();
        Logger.Info(nameof(HorizonProcess), $"进程已释放: PID={Info.ProcessId}");
    }
}
