using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 APM 服务状态
/// </summary>
public sealed class ApmState
{
    /// <summary>当前性能模式 (0=Handheld, 1=Docked)</summary>
    private uint _performanceMode;

    /// <summary>当前性能配置 (Handheld)</summary>
    private uint _handheldConfig = 0x000A0000; // 默认手持模式配置

    /// <summary>当前性能配置 (Docked)</summary>
    private uint _dockedConfig = 0x000C0000; // 默认底座模式配置

    /// <summary>CPU 超频是否启用</summary>
    private bool _cpuOverclockEnabled;

    /// <summary>CPU 增强模式 (0=None, 1=Boost, 2=Full)</summary>
    private uint _cpuBoostMode;

    /// <summary>当前性能模式</summary>
    public uint PerformanceMode
    {
        get => _performanceMode;
        set => _performanceMode = value;
    }

    /// <summary>手持模式性能配置</summary>
    public uint HandheldConfig
    {
        get => _handheldConfig;
        set => _handheldConfig = value;
    }

    /// <summary>底座模式性能配置</summary>
    public uint DockedConfig
    {
        get => _dockedConfig;
        set => _dockedConfig = value;
    }

    /// <summary>CPU 超频是否启用</summary>
    public bool CpuOverclockEnabled
    {
        get => _cpuOverclockEnabled;
        set => _cpuOverclockEnabled = value;
    }

    /// <summary>CPU 增强模式</summary>
    public uint CpuBoostMode
    {
        get => _cpuBoostMode;
        set => _cpuBoostMode = value;
    }

    /// <summary>根据性能模式获取当前配置</summary>
    public uint GetCurrentConfig() => _performanceMode == 0 ? _handheldConfig : _dockedConfig;
}

/// <summary>
/// apm — 性能管理服务 (IManager)
/// nn::apm::IManager
/// libnx appletInitialize() 内部隐式调用此服务
/// 提供性能会话管理和性能模式查询
/// 命令表基于 SwitchBrew APM_services 页面
/// </summary>
public sealed class ApmService : IIpcService
{
    public string PortName => "apm";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly ApmState _state;

    public ApmService(ApmState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = OpenSession,           // 打开 ISession 会话
            [1] = GetPerformanceMode,     // 获取当前性能模式
            [6] = IsCpuOverclockEnabled,  // 是否 CPU 超频
        };
    }

    /// <summary>命令 0: OpenSession — 打开 ISession 会话</summary>
    private ResultCode OpenSession(IpcRequest request, ref IpcResponse response)
    {
        int sessionHandle = unchecked((int)0xFFFF0F00);
        response.Data.AddRange(BitConverter.GetBytes(sessionHandle));
        Logger.Debug(PortName, $"{PortName}: OpenSession → ISession handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetPerformanceMode — 获取当前性能模式</summary>
    private ResultCode GetPerformanceMode(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.PerformanceMode));
        Logger.Debug(PortName, $"{PortName}: GetPerformanceMode → {_state.PerformanceMode}");
        return ResultCode.Success;
    }

    /// <summary>命令 6: IsCpuOverclockEnabled — 是否 CPU 超频</summary>
    private ResultCode IsCpuOverclockEnabled(IpcRequest request, ref IpcResponse response)
    {
        response.Data.Add((byte)(_state.CpuOverclockEnabled ? 1 : 0));
        Logger.Debug(PortName, $"{PortName}: IsCpuOverclockEnabled → {_state.CpuOverclockEnabled}");
        return ResultCode.Success;
    }

    internal ApmState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// apm:sys — 性能管理服务 (系统管理端口)
/// nn::apm::ISystemManager
/// 供系统模块控制性能策略和 CPU 增强模式
/// 命令表基于 SwitchBrew APM_services 页面
/// </summary>
public sealed class ApmSysService : IIpcService
{
    public string PortName => "apm:sys";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly ApmState _state;

    public ApmSysService(ApmState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = RequestPerformanceMode,         // 请求性能模式
            [1] = GetPerformanceEvent,             // 获取性能事件 (stub)
            [2] = GetThrottlingState,              // 获取降频状态 (stub)
            [3] = GetLastThrottlingState,           // 获取上次降频状态 (stub)
            [4] = ClearLastThrottlingState,        // 清除上次降频状态 (stub)
            [5] = LoadAndApplySettings,            // [5.0.0+] 加载并应用设置 (stub)
            [6] = SetCpuBoostMode,                // [7.0.0+] 设置 CPU 增强模式
            [7] = GetCurrentPerformanceConfiguration, // 获取当前性能配置
        };
    }

    /// <summary>命令 0: RequestPerformanceMode — 请求性能模式</summary>
    private ResultCode RequestPerformanceMode(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.ApmResult(2);
        uint mode = BitConverter.ToUInt32(request.Data, 0);
        _state.PerformanceMode = mode;
        Logger.Debug(PortName, $"{PortName}: RequestPerformanceMode → mode={mode}");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetPerformanceEvent — 获取性能事件 (stub)</summary>
    private ResultCode GetPerformanceEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF0F10);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, $"{PortName}: GetPerformanceEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetThrottlingState — 获取降频状态 (stub)</summary>
    private ResultCode GetThrottlingState(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // 未降频
        Logger.Debug(PortName, $"{PortName}: GetThrottlingState → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetLastThrottlingState — 获取上次降频状态 (stub)</summary>
    private ResultCode GetLastThrottlingState(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(PortName, $"{PortName}: GetLastThrottlingState → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 4: ClearLastThrottlingState — 清除上次降频状态 (stub)</summary>
    private ResultCode ClearLastThrottlingState(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ClearLastThrottlingState (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 5: LoadAndApplySettings — [5.0.0+] 加载并应用设置 (stub)</summary>
    private ResultCode LoadAndApplySettings(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: LoadAndApplySettings (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 6: SetCpuBoostMode — [7.0.0+] 设置 CPU 增强模式</summary>
    private ResultCode SetCpuBoostMode(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.ApmResult(2);
        uint boostMode = BitConverter.ToUInt32(request.Data, 0);
        _state.CpuBoostMode = boostMode;
        Logger.Debug(PortName, $"{PortName}: SetCpuBoostMode → {boostMode}");
        return ResultCode.Success;
    }

    /// <summary>命令 7: GetCurrentPerformanceConfiguration — 获取当前性能配置</summary>
    private ResultCode GetCurrentPerformanceConfiguration(IpcRequest request, ref IpcResponse response)
    {
        uint config = _state.GetCurrentConfig();
        response.Data.AddRange(BitConverter.GetBytes(config));
        Logger.Debug(PortName, $"{PortName}: GetCurrentPerformanceConfiguration → 0x{config:X8}");
        return ResultCode.Success;
    }

    internal ApmState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// apm:p — 性能管理服务 (特权端口, Pre-8.0.0)
/// nn::apm::IManagerPrivileged
/// 在 8.0.0 之前提供 OpenSession，之后被合并到 apm
/// 仅 1 个命令，与 apm OpenSession 相同
/// 命令表基于 SwitchBrew APM_services 页面
/// </summary>
public sealed class ApmPService : IIpcService
{
    public string PortName => "apm:p";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly ApmState _state;

    public ApmPService(ApmState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = OpenSession, // 打开 ISession 会话
        };
    }

    /// <summary>命令 0: OpenSession — 打开 ISession 会话</summary>
    private ResultCode OpenSession(IpcRequest request, ref IpcResponse response)
    {
        int sessionHandle = unchecked((int)0xFFFF0F20);
        response.Data.AddRange(BitConverter.GetBytes(sessionHandle));
        Logger.Debug(PortName, $"{PortName}: OpenSession → ISession handle");
        return ResultCode.Success;
    }

    internal ApmState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// apm:session — ISession 性能会话
/// nn::apm::ISession
/// 通过 apm 的 OpenSession 命令获取
/// 管理 Applet 级别的性能配置
/// 命令表基于 SwitchBrew APM_services 页面
/// </summary>
public sealed class ApmSessionService : IIpcService
{
    public string PortName => "apm:sess"; // 内部虚拟端口名 — 通过 OpenSession 获取

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly ApmState _state;

    public ApmSessionService(ApmState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = SetPerformanceConfiguration,  // 设置性能配置
            [1] = GetPerformanceConfiguration,  // 获取性能配置
            [2] = SetCpuOverclockEnabled,       // 设置 CPU 超频
        };
    }

    /// <summary>命令 0: SetPerformanceConfiguration — 设置性能配置</summary>
    private ResultCode SetPerformanceConfiguration(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.ApmResult(2);
        uint mode = BitConverter.ToUInt32(request.Data, 0);
        uint config = BitConverter.ToUInt32(request.Data, 4);
        if (mode == 0)
            _state.HandheldConfig = config;
        else
            _state.DockedConfig = config;
        Logger.Debug(PortName, $"{PortName}: SetPerformanceConfiguration(mode={mode}, config=0x{config:X8})");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetPerformanceConfiguration — 获取性能配置</summary>
    private ResultCode GetPerformanceConfiguration(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.ApmResult(2);
        uint mode = BitConverter.ToUInt32(request.Data, 0);
        uint config = mode == 0 ? _state.HandheldConfig : _state.DockedConfig;
        response.Data.AddRange(BitConverter.GetBytes(config));
        Logger.Debug(PortName, $"{PortName}: GetPerformanceConfiguration(mode={mode}) → 0x{config:X8}");
        return ResultCode.Success;
    }

    /// <summary>命令 2: SetCpuOverclockEnabled — 设置 CPU 超频</summary>
    private ResultCode SetCpuOverclockEnabled(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 1) return ResultCode.ApmResult(2);
        bool enabled = request.Data[0] != 0;
        _state.CpuOverclockEnabled = enabled;
        Logger.Debug(PortName, $"{PortName}: SetCpuOverclockEnabled → {enabled}");
        return ResultCode.Success;
    }

    internal ApmState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}
