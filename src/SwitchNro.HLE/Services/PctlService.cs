using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的家长控制状态
/// </summary>
public sealed class PctlState
{
    /// <summary>是否已初始化</summary>
    private bool _initialized;

    /// <summary>是否启用家长控制</summary>
    private bool _parentalControlEnabled;

    /// <summary>是否允许自由通信（在线功能）</summary>
    private bool _freeCommunicationEnabled = true; // 默认允许（模拟器不需要限制）

    /// <summary>是否允许 SNS 好友联系</summary>
    private bool _snsFriendContactPermitted = true;

    /// <summary>当前限制模式: 0=None, 1=RestrictionMode</summary>
    private uint _restrictionMode;

    /// <summary>是否已初始化</summary>
    public bool Initialized
    {
        get => _initialized;
        set => _initialized = value;
    }

    /// <summary>是否启用家长控制</summary>
    public bool ParentalControlEnabled
    {
        get => _parentalControlEnabled;
        set => _parentalControlEnabled = value;
    }

    /// <summary>是否允许自由通信</summary>
    public bool FreeCommunicationEnabled
    {
        get => _freeCommunicationEnabled;
        set => _freeCommunicationEnabled = value;
    }

    /// <summary>是否允许 SNS 好友联系</summary>
    public bool SnsFriendContactPermitted
    {
        get => _snsFriendContactPermitted;
        set => _snsFriendContactPermitted = value;
    }

    /// <summary>限制模式</summary>
    public uint RestrictionMode
    {
        get => _restrictionMode;
        set => _restrictionMode = value;
    }
}

/// <summary>
/// pctl:s — 家长控制服务 (标准端口)
/// nn::pctl::IParentalControlServiceFactory
/// 提供 IParentalControlService 实例创建
/// </summary>
public sealed class PctlSService : IIpcService
{
    public string PortName => "pctl:s";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PctlState _state;

    public PctlSService(PctlState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateService,                  // 创建 IParentalControlService（含初始化）
            [1] = CreateServiceWithoutInitialize, // 创建 IParentalControlService（不含初始化）
        };
    }

    /// <summary>命令 0: CreateService — 创建 IParentalControlService 并初始化</summary>
    private ResultCode CreateService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.PctlResult(2); // Invalid size

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        _state.Initialized = true;

        int handle = unchecked((int)0xFFFF0700);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(PctlSService), $"pctl:s: CreateService(pid=0x{pid:X16}) → IParentalControlService handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: CreateServiceWithoutInitialize — 创建 IParentalControlService（不初始化）</summary>
    private ResultCode CreateServiceWithoutInitialize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.PctlResult(2);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        // 不设置 _initialized = true

        int handle = unchecked((int)0xFFFF0701);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(PctlSService), $"pctl:s: CreateServiceWithoutInitialize(pid=0x{pid:X16})");
        return ResultCode.Success;
    }

    internal PctlState State => _state;

    public void Dispose() { }
}

/// <summary>
/// pctl:r — 家长控制服务 (只读端口)
/// 仅提供只读查询，不支持修改
/// </summary>
public sealed class PctlRService : IIpcService
{
    public string PortName => "pctl:r";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PctlState _state;

    public PctlRService(PctlState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateService,
            [1] = CreateServiceWithoutInitialize,
        };
    }

    private ResultCode CreateService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.PctlResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        _state.Initialized = true;
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0700)));
        Logger.Debug(nameof(PctlRService), $"pctl:r: CreateService(pid=0x{pid:X16})");
        return ResultCode.Success;
    }

    private ResultCode CreateServiceWithoutInitialize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.PctlResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0701)));
        Logger.Debug(nameof(PctlRService), $"pctl:r: CreateServiceWithoutInitialize(pid=0x{pid:X16})");
        return ResultCode.Success;
    }

    internal PctlState State => _state;

    public void Dispose() { }
}

/// <summary>
/// pctl:a — 家长控制服务 (管理员端口)
/// 仅限系统进程使用
/// </summary>
public sealed class PctlAService : IIpcService
{
    public string PortName => "pctl:a";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PctlState _state;

    public PctlAService(PctlState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateService,
            [1] = CreateServiceWithoutInitialize,
        };
    }

    private ResultCode CreateService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.PctlResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        _state.Initialized = true;
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0700)));
        Logger.Debug(nameof(PctlAService), $"pctl:a: CreateService(pid=0x{pid:X16})");
        return ResultCode.Success;
    }

    private ResultCode CreateServiceWithoutInitialize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.PctlResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0701)));
        Logger.Debug(nameof(PctlAService), $"pctl:a: CreateServiceWithoutInitialize(pid=0x{pid:X16})");
        return ResultCode.Success;
    }

    internal PctlState State => _state;

    public void Dispose() { }
}

/// <summary>
/// IParentalControlService — 家长控制服务接口
/// nn::pctl::IParentalControlService
/// 通过 pctl:s/r/a 的 CreateService 获取
/// 提供家长控制状态查询、自由通信权限等功能
/// </summary>
public sealed class PctlControlService : IIpcService
{
    public string PortName => "pctl:ctrl"; // 内部虚拟端口名 — 通过 CreateService 获取，不注册为命名端口

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PctlState _state;

    public PctlControlService(PctlState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [1]    = Initialize,                        // 初始化
            [1001] = IsFreeCommunicationEnabled,        // 是否允许自由通信
            [1002] = IsFreeCommunicationEnabled2,       // 是否允许自由通信 (变体)
            [1003] = ConfirmSynchronousOfflineDeviceHash, // 确认同步离线设备哈希
            [1004] = GetSnsAccountFriendPersonIdList,   // 获取 SNS 好友列表 (stub)
            [1010] = IsRestrictionEnabled,              // 是否启用限制
            [1011] = IsRestrictionEnabled2,             // 是否启用限制 (变体)
            [1012] = GetCurrentRestriction,             // 获取当前限制模式
            [1013] = GetFreeCommunicationApplicationList, // 获取自由通信应用列表 (stub)
            [1014] = ConfirmApplicationAge,             // 确认应用年龄限制 (stub)
            [1016] = EndFreeCommunication,              // 结束自由通信
            [1017] = EndFreeCommunication2,             // 结束自由通信 (变体)
            [1032] = IsPairingActive,                   // 是否正在配对 (stub)
            [1042] = GetPlayTimerSettings,              // 获取游戏时间设置 (stub)
            [1046] = GetPlayTimerSettings2,             // 获取游戏时间设置 (变体, stub)
            [1061] = IsRestrictionTemporaryUnlocked,     // 是否临时解锁限制
        };
    }

    /// <summary>命令 1: Initialize — 初始化家长控制服务</summary>
    private ResultCode Initialize(IpcRequest request, ref IpcResponse response)
    {
        _state.Initialized = true;
        Logger.Debug(nameof(PctlControlService), "pctl:ctrl: Initialize");
        return ResultCode.Success;
    }

    /// <summary>命令 1001: IsFreeCommunicationEnabled — 是否允许自由通信</summary>
    private ResultCode IsFreeCommunicationEnabled(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized)
            return ResultCode.PctlResult(8); // Not initialized

        response.Data.AddRange(BitConverter.GetBytes(_state.FreeCommunicationEnabled ? 1U : 0U));
        Logger.Debug(nameof(PctlControlService), $"pctl:ctrl: IsFreeCommunicationEnabled → {_state.FreeCommunicationEnabled}");
        return ResultCode.Success;
    }

    /// <summary>命令 1002: IsFreeCommunicationEnabled2 — 是否允许自由通信 (变体)</summary>
    private ResultCode IsFreeCommunicationEnabled2(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized)
            return ResultCode.PctlResult(8);

        response.Data.AddRange(BitConverter.GetBytes(_state.FreeCommunicationEnabled ? 1U : 0U));
        return ResultCode.Success;
    }

    /// <summary>命令 1003: ConfirmSynchronousOfflineDeviceHash — 确认同步离线设备哈希 (stub)</summary>
    private ResultCode ConfirmSynchronousOfflineDeviceHash(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PctlControlService), "pctl:ctrl: ConfirmSynchronousOfflineDeviceHash (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 1004: GetSnsAccountFriendPersonIdList — 获取 SNS 好友列表 (stub)</summary>
    private ResultCode GetSnsAccountFriendPersonIdList(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(0)); // count = 0
        Logger.Debug(nameof(PctlControlService), "pctl:ctrl: GetSnsAccountFriendPersonIdList → 0");
        return ResultCode.Success;
    }

    /// <summary>命令 1010: IsRestrictionEnabled — 是否启用家长控制限制</summary>
    private ResultCode IsRestrictionEnabled(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(_state.ParentalControlEnabled ? 1U : 0U));
        Logger.Debug(nameof(PctlControlService), $"pctl:ctrl: IsRestrictionEnabled → {_state.ParentalControlEnabled}");
        return ResultCode.Success;
    }

    /// <summary>命令 1011: IsRestrictionEnabled2 — 是否启用限制 (变体)</summary>
    private ResultCode IsRestrictionEnabled2(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(_state.ParentalControlEnabled ? 1U : 0U));
        return ResultCode.Success;
    }

    /// <summary>命令 1012: GetCurrentRestriction — 获取当前限制模式</summary>
    private ResultCode GetCurrentRestriction(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(_state.RestrictionMode));
        Logger.Debug(nameof(PctlControlService), $"pctl:ctrl: GetCurrentRestriction → {_state.RestrictionMode}");
        return ResultCode.Success;
    }

    /// <summary>命令 1013: GetFreeCommunicationApplicationList — 获取自由通信应用列表 (stub)</summary>
    private ResultCode GetFreeCommunicationApplicationList(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(0)); // count = 0
        return ResultCode.Success;
    }

    /// <summary>命令 1014: ConfirmApplicationAge — 确认应用年龄限制 (stub)</summary>
    private ResultCode ConfirmApplicationAge(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(PctlControlService), "pctl:ctrl: ConfirmApplicationAge (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 1016: EndFreeCommunication — 结束自由通信</summary>
    private ResultCode EndFreeCommunication(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        _state.FreeCommunicationEnabled = false;
        Logger.Debug(nameof(PctlControlService), "pctl:ctrl: EndFreeCommunication");
        return ResultCode.Success;
    }

    /// <summary>命令 1017: EndFreeCommunication2 — 结束自由通信 (变体)</summary>
    private ResultCode EndFreeCommunication2(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        _state.FreeCommunicationEnabled = false;
        return ResultCode.Success;
    }

    /// <summary>命令 1032: IsPairingActive — 是否正在配对 (stub)</summary>
    private ResultCode IsPairingActive(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(0U)); // 不在配对
        return ResultCode.Success;
    }

    /// <summary>命令 1042: GetPlayTimerSettings — 获取游戏时间设置 (stub)</summary>
    private ResultCode GetPlayTimerSettings(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(0U)); // 无限制
        return ResultCode.Success;
    }

    /// <summary>命令 1046: GetPlayTimerSettings2 — 获取游戏时间设置 (变体, stub)</summary>
    private ResultCode GetPlayTimerSettings2(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        response.Data.AddRange(BitConverter.GetBytes(0U));
        return ResultCode.Success;
    }

    /// <summary>命令 1061: IsRestrictionTemporaryUnlocked — 是否临时解锁限制</summary>
    private ResultCode IsRestrictionTemporaryUnlocked(IpcRequest request, ref IpcResponse response)
    {
        if (!_state.Initialized) return ResultCode.PctlResult(8);
        // 模拟器中始终返回未解锁
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(PctlControlService), "pctl:ctrl: IsRestrictionTemporaryUnlocked → false");
        return ResultCode.Success;
    }

    internal PctlState State => _state;

    public void Dispose() { }
}
