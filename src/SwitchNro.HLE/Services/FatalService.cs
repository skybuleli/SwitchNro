using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 Fatal 服务状态
/// </summary>
public sealed class FatalState
{
    /// <summary>最后发生的致命错误码</summary>
    private ulong _lastErrorCode;

    /// <summary>最后发生致命错误时的进程 ID</summary>
    private ulong _lastProcessId;

    /// <summary>是否发生过致命错误</summary>
    private bool _hasFatalOccurred;

    /// <summary>最后发生的致命错误码</summary>
    public ulong LastErrorCode
    {
        get => _lastErrorCode;
        set => _lastErrorCode = value;
    }

    /// <summary>最后发生致命错误时的进程 ID</summary>
    public ulong LastProcessId
    {
        get => _lastProcessId;
        set => _lastProcessId = value;
    }

    /// <summary>是否发生过致命错误</summary>
    public bool HasFatalOccurred
    {
        get => _hasFatalOccurred;
        set => _hasFatalOccurred = value;
    }

    /// <summary>记录致命错误</summary>
    public void RecordFatal(ulong errorCode, ulong processId)
    {
        _lastErrorCode = errorCode;
        _lastProcessId = processId;
        _hasFatalOccurred = true;
    }
}

/// <summary>
/// fatal:u — 致命错误服务 (用户端口)
/// nn::fatal::detail::IFatalService
/// Homebrew 启动时最先调用的服务之一，用于捕获和报告致命错误
/// 在模拟器中所有命令均为 stub — 仅记录错误，不终止进程
/// 命令表基于 SwitchBrew Fatal_services 页面
/// </summary>
public sealed class FatalUService : IIpcService
{
    public string PortName => "fatal:u";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly FatalState _state;

    public FatalUService(FatalState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = ThrowFatal,               // 抛出致命错误
            [1] = ThrowFatalWithPolicy,      // 带策略抛出致命错误
            [2] = ThrowFatalWithCpuContext,  // 带 CPU 上下文抛出致命错误
            [3] = ThrowFatalForManual,       // [21.0.0+] 手动致命错误
        };
    }

    /// <summary>命令 0: ThrowFatal — 抛出致命错误 (stub)</summary>
    private ResultCode ThrowFatal(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.FatalResult(2);
        ulong errorCode = BitConverter.ToUInt64(request.Data, 0);
        _state.RecordFatal(errorCode, 0);
        Logger.Warning(PortName, $"{PortName}: ThrowFatal(error=0x{errorCode:X16}) — stub, not terminating");
        return ResultCode.Success;
    }

    /// <summary>命令 1: ThrowFatalWithPolicy — 带策略抛出致命错误 (stub)</summary>
    private ResultCode ThrowFatalWithPolicy(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16) return ResultCode.FatalResult(2);
        ulong errorCode = BitConverter.ToUInt64(request.Data, 0);
        uint policy = BitConverter.ToUInt32(request.Data, 8);
        _state.RecordFatal(errorCode, 0);
        Logger.Warning(PortName, $"{PortName}: ThrowFatalWithPolicy(error=0x{errorCode:X16}, policy={policy}) — stub, not terminating");
        return ResultCode.Success;
    }

    /// <summary>命令 2: ThrowFatalWithCpuContext — 带 CPU 上下文抛出致命错误 (stub)</summary>
    private ResultCode ThrowFatalWithCpuContext(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16) return ResultCode.FatalResult(2);
        ulong errorCode = BitConverter.ToUInt64(request.Data, 0);
        _state.RecordFatal(errorCode, 0);
        Logger.Warning(PortName, $"{PortName}: ThrowFatalWithCpuContext(error=0x{errorCode:X16}) — stub, not terminating");
        return ResultCode.Success;
    }

    /// <summary>命令 3: ThrowFatalForManual — [21.0.0+] 手动致命错误 (stub)</summary>
    private ResultCode ThrowFatalForManual(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.FatalResult(2);
        ulong errorCode = BitConverter.ToUInt64(request.Data, 0);
        _state.RecordFatal(errorCode, 0);
        Logger.Warning(PortName, $"{PortName}: ThrowFatalForManual(error=0x{errorCode:X16}) — stub, not terminating");
        return ResultCode.Success;
    }

    internal FatalState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// fatal:p — 致命错误服务 (私有端口)
/// nn::fatal::detail::IPrivateService
/// 供系统模块查询致命错误事件
/// 命令表基于 SwitchBrew Fatal_services 页面
/// </summary>
public sealed class FatalPService : IIpcService
{
    public string PortName => "fatal:p";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly FatalState _state;

    public FatalPService(FatalState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = GetFatalEvent,    // 获取致命错误事件句柄
            [10] = GetFatalContext,  // [14.0.0+] 获取致命错误上下文
        };
    }

    /// <summary>命令 0: GetFatalEvent — 获取致命错误事件句柄</summary>
    private ResultCode GetFatalEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF0E00);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, $"{PortName}: GetFatalEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetFatalContext — [14.0.0+] 获取致命错误上下文</summary>
    private ResultCode GetFatalContext(IpcRequest request, ref IpcResponse response)
    {
        // 返回 FatalContext 结构 (0x250 bytes) — 全零 stub
        response.Data.AddRange(new byte[0x250]);
        Logger.Debug(PortName, $"{PortName}: GetFatalContext → zeros (stub)");
        return ResultCode.Success;
    }

    internal FatalState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}
