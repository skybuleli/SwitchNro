using System;
using System.Collections.Generic;
using System.Linq;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;
using SwitchNro.Horizon;

namespace SwitchNro.HLE.Services;

/// <summary>
/// pm:dmnt — 进程调试监控服务 (Process Debug Monitor)
/// 提供进程调试接口：启动进程、获取进程ID、挂钩创建事件
/// Homebrew 调试器 (如 gdbstub) 通过此服务获取目标进程信息
/// </summary>
public sealed class PmDmntService : IIpcService
{
    public string PortName => "pm:dmnt";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>关联的 HorizonSystem 引用（用于查询进程信息）</summary>
    private readonly HorizonSystem? _system;

    public PmDmntService(HorizonSystem? system = null)
    {
        _system = system;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetModuleIdList,               // [1.0.0-4.1.0] 获取模块 ID 列表 (retail 固件中已 stub)
            [1] = GetJitDebugProcessIdList,      // 获取 JIT 调试进程 ID 列表
            [2] = StartProcess,                  // 启动指定进程
            [3] = GetProcessId,                  // 通过 TitleId 获取 ProcessId
            [4] = HookToCreateProcess,           // 挂钩进程创建事件
            [5] = GetApplicationProcessId,       // 获取应用程序进程 ID
            [6] = HookToCreateApplicationProcess, // 挂钩应用程序创建事件 / ClearHook [6.0.0+]
            [7] = GetProgramId,                  // [14.0.0+] 获取程序 ID
        };
    }

    /// <summary>命令 0: GetModuleIdList — 已在 retail 固件中 stub</summary>
    private ResultCode GetModuleIdList(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmDmntService), "pm:dmnt: GetModuleIdList (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetJitDebugProcessIdList — 获取 JIT 调试进程 ID 列表</summary>
    private ResultCode GetJitDebugProcessIdList(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmDmntService), "pm:dmnt: GetJitDebugProcessIdList");
        response.Data.AddRange(BitConverter.GetBytes(0)); // count = 0
        return ResultCode.Success;
    }

    /// <summary>命令 2: StartProcess — 启动指定 PID 的进程</summary>
    private ResultCode StartProcess(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3); // Invalid argument

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Info(nameof(PmDmntService), $"pm:dmnt: StartProcess(PID={pid})");

        if (_system != null)
        {
            var process = _system.GetAllProcesses().FirstOrDefault(p => p.Info.ProcessId == pid);
            if (process != null && process.State == ProcessState.Created)
            {
                _system.StartProcess(process);
            }
        }

        return ResultCode.Success;
    }

    /// <summary>命令 3: GetProcessId — 通过 TitleId 查询 ProcessId</summary>
    private ResultCode GetProcessId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong titleId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(PmDmntService), $"pm:dmnt: GetProcessId(TitleId=0x{titleId:X16})");

        ulong pid = 0;
        if (_system != null)
        {
            var process = _system.GetAllProcesses().FirstOrDefault(p => p.Info.TitleId == titleId);
            pid = process?.Info.ProcessId ?? 0;
        }

        if (pid == 0)
        {
            Logger.Warning(nameof(PmDmntService), $"pm:dmnt: 未找到 TitleId=0x{titleId:X16} 对应的进程");
            return ResultCode.PmResult(2); // Process not found
        }

        response.Data.AddRange(BitConverter.GetBytes(pid));
        return ResultCode.Success;
    }

    /// <summary>命令 4: HookToCreateProcess — 挂钩指定 TitleId 的进程创建事件</summary>
    private ResultCode HookToCreateProcess(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong titleId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(PmDmntService), $"pm:dmnt: HookToCreateProcess(TitleId=0x{titleId:X16})");
        // 返回虚拟事件句柄
        response.Data.AddRange(BitConverter.GetBytes(0xFFFF0001));
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetApplicationProcessId — 获取当前应用程序进程 ID</summary>
    private ResultCode GetApplicationProcessId(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmDmntService), "pm:dmnt: GetApplicationProcessId");

        ulong appPid = 0;
        if (_system?.ActiveProcess != null)
        {
            appPid = _system.ActiveProcess.Info.ProcessId;
        }

        if (appPid == 0)
        {
            Logger.Warning(nameof(PmDmntService), "pm:dmnt: 无活跃应用程序进程");
            return ResultCode.PmResult(2);
        }

        response.Data.AddRange(BitConverter.GetBytes(appPid));
        return ResultCode.Success;
    }

    /// <summary>命令 6: HookToCreateApplicationProcess / ClearHook [6.0.0+]</summary>
    private ResultCode HookToCreateApplicationProcess(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length >= 4)
        {
            // ClearHook 模式 [6.0.0+]
            uint bitflags = BitConverter.ToUInt32(request.Data, 0);
            Logger.Debug(nameof(PmDmntService), $"pm:dmnt: ClearHook(flags=0x{bitflags:X8})");
        }
        else
        {
            Logger.Debug(nameof(PmDmntService), "pm:dmnt: HookToCreateApplicationProcess");
            response.Data.AddRange(BitConverter.GetBytes(0xFFFF0002)); // 虚拟事件句柄
        }

        return ResultCode.Success;
    }

    /// <summary>命令 7: GetProgramId — [14.0.0+] 获取程序 ID</summary>
    private ResultCode GetProgramId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(PmDmntService), $"pm:dmnt: GetProgramId(PID={pid})");

        ulong titleId = 0;
        if (_system != null)
        {
            var process = _system.GetAllProcesses().FirstOrDefault(p => p.Info.ProcessId == pid);
            titleId = process?.Info.TitleId ?? 0;
        }

        if (titleId == 0)
        {
            return ResultCode.PmResult(2);
        }

        response.Data.AddRange(BitConverter.GetBytes(titleId));
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// pm:info — 进程信息服务 (Process Information)
/// 提供只读的进程信息查询接口
/// </summary>
public sealed class PmInfoService : IIpcService
{
    public string PortName => "pm:info";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>关联的 HorizonSystem 引用</summary>
    private readonly HorizonSystem? _system;

    public PmInfoService(HorizonSystem? system = null)
    {
        _system = system;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetProgramId,                              // 通过 PID 获取 TitleId
            [1] = GetAppletCurrentResourceLimitValues,       // [14.0.0+]
            [2] = GetAppletPeakResourceLimitValues,          // [14.0.0+]
        };
    }

    /// <summary>命令 0: GetProgramId — 通过 ProcessId 获取 TitleId</summary>
    private ResultCode GetProgramId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(PmInfoService), $"pm:info: GetProgramId(PID={pid})");

        ulong titleId = 0;
        if (_system != null)
        {
            var process = _system.GetAllProcesses().FirstOrDefault(p => p.Info.ProcessId == pid);
            titleId = process?.Info.TitleId ?? 0;
        }

        if (titleId == 0)
        {
            Logger.Warning(nameof(PmInfoService), $"pm:info: 未找到 PID={pid} 对应的进程");
            return ResultCode.PmResult(2);
        }

        response.Data.AddRange(BitConverter.GetBytes(titleId));
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetAppletCurrentResourceLimitValues — [14.0.0+]</summary>
    private ResultCode GetAppletCurrentResourceLimitValues(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmInfoService), "pm:info: GetAppletCurrentResourceLimitValues");
        // 返回默认资源限制值 (内存 0x1A000000 = 448MB, 线程 512)
        response.Data.AddRange(BitConverter.GetBytes(0x1A000000UL)); // 内存限制
        response.Data.AddRange(BitConverter.GetBytes(512U));         // 线程限制
        response.Data.AddRange(BitConverter.GetBytes(0UL));          // 事件限制
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetAppletPeakResourceLimitValues — [14.0.0+]</summary>
    private ResultCode GetAppletPeakResourceLimitValues(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmInfoService), "pm:info: GetAppletPeakResourceLimitValues");
        response.Data.AddRange(BitConverter.GetBytes(0x1A000000UL)); // 内存峰值
        response.Data.AddRange(BitConverter.GetBytes(512U));         // 线程峰值
        response.Data.AddRange(BitConverter.GetBytes(0UL));          // 事件峰值
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// pm:shell — 进程管理 Shell 服务 (Process Shell)
/// 提供进程启动、终止、事件管理等特权操作
/// 仅限系统模块使用，Homebrew 通常不直接调用
/// </summary>
public sealed class PmShellService : IIpcService
{
    public string PortName => "pm:shell";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>关联的 HorizonSystem 引用</summary>
    private readonly HorizonSystem? _system;

    /// <summary>是否已完成启动通知（由 NotifyBootFinished 设置，供 GetBootFinishedEventHandle 参考使用）</summary>
    private bool _bootFinished;

    /// <summary>外部可查询启动完成状态</summary>
    public bool IsBootFinished => _bootFinished;

    /// <summary>下一个虚拟进程句柄计数器</summary>
    private ulong _nextLaunchedPid = 0x80;

    public PmShellService(HorizonSystem? system = null)
    {
        _system = system;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = LaunchProgram,                        // 启动程序
            [1]  = TerminateProcess,                     // 终止进程
            [2]  = TerminateProgram,                     // 终止程序
            [3]  = GetProcessEventHandle,                // 获取进程事件句柄
            [4]  = GetProcessEventInfo,                  // 获取进程事件信息
            [5]  = CleanupProcess,                       // [1.0.0-4.1.0] 清理已终止进程
            [6]  = ClearJitDebugOccured,                 // [1.0.0-4.1.0] 清除 JIT 调试标志
            [7]  = NotifyBootFinished,                   // 通知启动完成
            [8]  = GetApplicationProcessIdForShell,      // 获取应用程序 PID (Shell 版)
            [9]  = BoostApplicationThreadResourceLimit,   // [7.0.0+] 提升应用线程资源限制
            [10] = GetBootFinishedEventHandle,            // [8.0.0+] 获取启动完成事件句柄
        };
    }

    /// <summary>命令 0: LaunchProgram — 启动程序</summary>
    private ResultCode LaunchProgram(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16)
            return ResultCode.SfResult(3);

        uint launchFlags = BitConverter.ToUInt32(request.Data, 0);
        ulong programId = BitConverter.ToUInt64(request.Data, 8);
        Logger.Info(nameof(PmShellService), $"pm:shell: LaunchProgram(flags=0x{launchFlags:X8}, programId=0x{programId:X16})");

        ulong pid = _nextLaunchedPid++;
        response.Data.AddRange(BitConverter.GetBytes(pid));

        return ResultCode.Success;
    }

    /// <summary>命令 1: TerminateProcess — 终止指定 PID 的进程</summary>
    private ResultCode TerminateProcess(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Info(nameof(PmShellService), $"pm:shell: TerminateProcess(PID={pid})");

        _system?.TerminateProcess(pid);
        return ResultCode.Success;
    }

    /// <summary>命令 2: TerminateProgram — 终止程序</summary>
    private ResultCode TerminateProgram(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Info(nameof(PmShellService), $"pm:shell: TerminateProgram(PID={pid})");

        _system?.TerminateProcess(pid);
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetProcessEventHandle — 获取进程事件句柄</summary>
    private ResultCode GetProcessEventHandle(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmShellService), "pm:shell: GetProcessEventHandle");
        response.Data.AddRange(BitConverter.GetBytes(0xFFFF0100)); // 虚拟可等待事件句柄
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetProcessEventInfo — 获取进程事件信息</summary>
    private ResultCode GetProcessEventInfo(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmShellService), "pm:shell: GetProcessEventInfo");

        if (_system?.ActiveProcess != null)
        {
            var state = _system.ActiveProcess.State;
            var eventType = state switch
            {
                ProcessState.Running => PmProcessEventInfo.Started,
                ProcessState.Exiting => PmProcessEventInfo.Exiting,
                ProcessState.Exited => PmProcessEventInfo.Exited,
                ProcessState.Crashed => PmProcessEventInfo.Exception,
                ProcessState.Paused => PmProcessEventInfo.DebugSuspended,
                _ => PmProcessEventInfo.Created,
            };
            response.Data.AddRange(BitConverter.GetBytes((uint)eventType));
            response.Data.AddRange(BitConverter.GetBytes(_system.ActiveProcess.Info.ProcessId));
        }
        else
        {
            response.Data.AddRange(BitConverter.GetBytes((uint)PmProcessEventInfo.Created));
            response.Data.AddRange(BitConverter.GetBytes(0UL)); // PID = 0
        }

        return ResultCode.Success;
    }

    /// <summary>命令 5: CleanupProcess — [1.0.0-4.1.0] 清理已终止进程</summary>
    private ResultCode CleanupProcess(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(PmShellService), $"pm:shell: CleanupProcess(PID={pid})");
        return ResultCode.Success;
    }

    /// <summary>命令 6: ClearJitDebugOccured — [1.0.0-4.1.0] 清除 JIT 调试标志</summary>
    private ResultCode ClearJitDebugOccured(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SfResult(3);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(PmShellService), $"pm:shell: ClearJitDebugOccured(PID={pid})");
        return ResultCode.Success;
    }

    /// <summary>命令 7: NotifyBootFinished — 通知启动完成</summary>
    private ResultCode NotifyBootFinished(IpcRequest request, ref IpcResponse response)
    {
        _bootFinished = true;
        Logger.Info(nameof(PmShellService), "pm:shell: NotifyBootFinished → Boot 完成");
        return ResultCode.Success;
    }

    /// <summary>命令 8: GetApplicationProcessIdForShell — 获取应用程序 PID</summary>
    private ResultCode GetApplicationProcessIdForShell(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmShellService), "pm:shell: GetApplicationProcessIdForShell");

        ulong appPid = 0;
        if (_system?.ActiveProcess != null)
        {
            appPid = _system.ActiveProcess.Info.ProcessId;
        }

        if (appPid == 0)
        {
            return ResultCode.PmResult(2);
        }

        response.Data.AddRange(BitConverter.GetBytes(appPid));
        return ResultCode.Success;
    }

    /// <summary>命令 9: BoostApplicationThreadResourceLimit — [7.0.0+]</summary>
    private ResultCode BoostApplicationThreadResourceLimit(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmShellService), "pm:shell: BoostApplicationThreadResourceLimit");
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetBootFinishedEventHandle — [8.0.0+]</summary>
    private ResultCode GetBootFinishedEventHandle(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmShellService), "pm:shell: GetBootFinishedEventHandle");
        response.Data.AddRange(BitConverter.GetBytes(0xFFFF0200)); // 虚拟事件句柄
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// pm:bm — 启动模式服务 (Boot Mode Interface)
/// 提供启动模式查询和维护模式设置
/// </summary>
public sealed class PmBmService : IIpcService
{
    public string PortName => "pm:bm";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>当前启动模式</summary>
    private BootMode _bootMode = BootMode.Normal;

    public PmBmService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetBootMode,              // 获取启动模式
            [1] = SetMaintenanceBoot,       // 设置维护模式启动
        };
    }

    /// <summary>命令 0: GetBootMode — 获取当前启动模式</summary>
    private ResultCode GetBootMode(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PmBmService), $"pm:bm: GetBootMode → {_bootMode}");
        response.Data.Add((byte)_bootMode);
        return ResultCode.Success;
    }

    /// <summary>命令 1: SetMaintenanceBoot — 设置维护模式启动</summary>
    private ResultCode SetMaintenanceBoot(IpcRequest request, ref IpcResponse response)
    {
        _bootMode = BootMode.Maintenance;
        Logger.Info(nameof(PmBmService), "pm:bm: SetMaintenanceBoot → 维护模式");
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>进程管理器事件信息类型</summary>
internal enum PmProcessEventInfo : uint
{
    Created = 0,
    Started = 1,
    Exited = 2,
    Exception = 3,
    DebugSuspended = 4,
    Exiting = 5,
    DebugStarted = 6,
    DebugAttached = 7,
}

/// <summary>启动模式</summary>
internal enum BootMode : byte
{
    Normal = 0,
    Maintenance = 1,
    SafeMode = 2,
}
