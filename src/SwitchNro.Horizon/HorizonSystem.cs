using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.Memory;
using SwitchNro.NroLoader;

namespace SwitchNro.Horizon;

/// <summary>
/// Horizon OS 模拟核心
/// 管理进程生命周期、线程调度、SVC 分发循环
/// </summary>
public sealed class HorizonSystem : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private readonly SvcDispatcher _svcDispatcher;
    private readonly Dictionary<ulong, HorizonProcess> _processes = new();
    private ulong _nextProcessId = 1;

    /// <summary>当前活跃进程</summary>
    public HorizonProcess? ActiveProcess { get; private set; }

    public HorizonSystem(VirtualMemoryManager memory, SvcDispatcher svcDispatcher)
    {
        _memory = memory;
        _svcDispatcher = svcDispatcher;
    }

    /// <summary>创建新进程并加载 NRO</summary>
    public HorizonProcess CreateProcess(NroModule nroModule, ProcessInfo info)
    {
        var processId = _nextProcessId++;
        var processInfo = info with { ProcessId = processId };

        var process = new HorizonProcess(
            processInfo,
            _memory,
            _svcDispatcher,
            nroModule);

        _processes[processId] = process;
        Logger.Info(nameof(HorizonSystem), $"创建进程 [{processInfo.Name}] PID={processId} 入口=0x{nroModule.EntryPoint:X16}");

        return process;
    }

    /// <summary>启动进程的主线程</summary>
    public void StartProcess(HorizonProcess process)
    {
        process.State = ProcessState.Running;
        ActiveProcess = process;

        // 设置 Homebrew ABI 环境
        // X0 = loader_config 结构指针
        // X1 = argv 数组指针
        process.Engine.SetRegister(0, 0); // 暂无 loader_config
        process.Engine.SetRegister(1, 0); // 暂无 argv

        // 分配主线程栈
        ulong stackBase = 0x0000_0200_0000UL; // 规格中定义的栈区域
        _memory.MapZero(stackBase, process.Info.MainStackSize, MemoryPermissions.ReadWrite);
        var stackTop = stackBase + process.Info.MainStackSize;
        process.Engine.SetSP(stackTop); // ARM64 栈向下增长

        Logger.Info(nameof(HorizonSystem), $"启动进程 PID={process.Info.ProcessId}, SP=0x{stackTop:X16}");
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

            // 将结果写回 vCPU 寄存器
            engine.SetSvcResult(svcResult.ReturnCode.IsSuccess ? 0UL : unchecked((ulong)svcResult.ReturnCode.GetHashCode()));

            // 继续执行
            result = engine.RunNext();
        }

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
