using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 PSC 服务状态
/// </summary>
public sealed class PscState
{
    /// <summary>当前电源状态 (0=Awake, 1=ReadySleep, 2=Sleep, 3=Shutdown, 4=ReadyAwake)</summary>
    private int _pmState = 4; // ReadyAwake — 模拟器启动后默认

    /// <summary>是否已初始化</summary>
    private bool _initialized;

    /// <summary>唤醒锁计数</summary>
    private int _wakeLockCount;

    /// <summary>当前电源状态</summary>
    public int PmState
    {
        get => _pmState;
        set => _pmState = value;
    }

    /// <summary>是否已初始化</summary>
    public bool Initialized
    {
        get => _initialized;
        set => _initialized = value;
    }

    /// <summary>唤醒锁计数</summary>
    public int WakeLockCount
    {
        get => _wakeLockCount;
        set => _wakeLockCount = value;
    }
}

/// <summary>
/// psc:m — 电源管理服务 (模块端口)
/// nn::psc::sf::IPmService
/// 提供 IPmModule 接口的获取
/// 命令表基于 SwitchBrew PSC_services 页面
/// </summary>
public sealed class PscMService : IIpcService
{
    public string PortName => "psc:m";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PscState _state;

    public PscMService(PscState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetPmModule, // 获取 IPmModule
        };
    }

    /// <summary>命令 0: GetPmModule — 获取 IPmModule 接口</summary>
    private ResultCode GetPmModule(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0D00);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, "psc:m: GetPmModule → IPmModule handle");
        return ResultCode.Success;
    }

    internal PscState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// IPmModule — 电源管理模块接口
/// nn::psc::sf::IPmModule
/// 通过 psc:m 的 GetPmModule 获取
/// 命令表基于 SwitchBrew PSC_services 页面
/// </summary>
public sealed class PmModuleService : IIpcService
{
    public string PortName => "psc:mod"; // 内部虚拟端口名

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PscState _state;

    public PmModuleService(PscState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = Initialize,              // 初始化电源管理模块
            [1]  = GetRequest,              // 获取电源请求 (stub)
            [2]  = Acknowledge,             // 确认电源请求 (stub)
            [3]  = Finalize,                // 终结电源管理模块 (stub)
            [4]  = AcknowledgeWithExpiry,    // 带超时确认 (stub)
            [5]  = TriggerEvent,            // 触发事件 (stub)
            [10] = CreateWakeLock,          // 创建唤醒锁
            [11] = DestroyWakeLock,         // 销毁唤醒锁
            [20] = GetAlarmEvent,           // 获取告警事件 (stub)
            [30] = Enable,                  // 启用电源通知
            [31] = Disable,                 // 禁用电源通知 (stub)
            [32] = IsEnabled,               // 是否已启用
        };
    }

    /// <summary>命令 0: Initialize — 初始化电源管理模块</summary>
    private ResultCode Initialize(IpcRequest request, ref IpcResponse response)
    {
        _state.Initialized = true;
        // 返回 KEvent handle 用于电源状态变更通知
        int eventHandle = unchecked((int)0xFFFF0D10);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, "psc:mod: Initialize → KEvent handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetRequest — 获取电源请求 (stub)</summary>
    private ResultCode GetRequest(IpcRequest request, ref IpcResponse response)
    {
        // 返回当前电源状态
        response.Data.AddRange(BitConverter.GetBytes((uint)_state.PmState));
        Logger.Debug(PortName, $"psc:mod: GetRequest → state={_state.PmState} (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: Acknowledge — 确认电源请求 (stub)</summary>
    private ResultCode Acknowledge(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:mod: Acknowledge (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 3: Finalize — 终结电源管理模块 (stub)</summary>
    private ResultCode Finalize(IpcRequest request, ref IpcResponse response)
    {
        _state.Initialized = false;
        Logger.Debug(PortName, "psc:mod: Finalize (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 4: AcknowledgeWithExpiry — 带超时确认 (stub)</summary>
    private ResultCode AcknowledgeWithExpiry(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:mod: AcknowledgeWithExpiry (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 5: TriggerEvent — 触发事件 (stub)</summary>
    private ResultCode TriggerEvent(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:mod: TriggerEvent (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10: CreateWakeLock — 创建唤醒锁</summary>
    private ResultCode CreateWakeLock(IpcRequest request, ref IpcResponse response)
    {
        _state.WakeLockCount++;
        Logger.Debug(PortName, $"psc:mod: CreateWakeLock → count={_state.WakeLockCount}");
        return ResultCode.Success;
    }

    /// <summary>命令 11: DestroyWakeLock — 销毁唤醒锁</summary>
    private ResultCode DestroyWakeLock(IpcRequest request, ref IpcResponse response)
    {
        if (_state.WakeLockCount > 0) _state.WakeLockCount--;
        Logger.Debug(PortName, $"psc:mod: DestroyWakeLock → count={_state.WakeLockCount}");
        return ResultCode.Success;
    }

    /// <summary>命令 20: GetAlarmEvent — 获取告警事件 (stub)</summary>
    private ResultCode GetAlarmEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF0D11);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, "psc:mod: GetAlarmEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 30: Enable — 启用电源通知</summary>
    private ResultCode Enable(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:mod: Enable (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 31: Disable — 禁用电源通知 (stub)</summary>
    private ResultCode Disable(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:mod: Disable (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 32: IsEnabled — 是否已启用</summary>
    private ResultCode IsEnabled(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.Initialized ? 1U : 0U));
        Logger.Debug(PortName, $"psc:mod: IsEnabled → {_state.Initialized}");
        return ResultCode.Success;
    }

    internal PscState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// psc:c — 电源管理控制服务 (控制端口)
/// nn::psc::sf::IPmControl
/// 提供系统级电源状态控制功能
/// 命令表基于 SwitchBrew PSC_services 页面
/// </summary>
public sealed class PscCService : IIpcService
{
    public string PortName => "psc:c";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PscState _state;

    public PscCService(PscState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = Initialize,                  // 初始化电源控制
            [1]  = DispatchRequest,             // 派发电源状态请求
            [2]  = GetResult,                   // 获取请求结果 (stub)
            [3]  = GetState,                    // 获取当前电源状态
            [4]  = Cancel,                      // 取消请求 (stub)
            [5]  = PrintModuleInformation,      // 打印模块信息 (stub)
            [6]  = GetModuleInformation,         // 获取模块信息 (stub)
            [7]  = SetRandomDelay,              // 设置随机延迟 (stub, 17.0.0+)
            [10] = GetStateLockUpdateEvent,     // 获取状态锁更新事件
            [11] = IsStateLocked,               // 是否状态已锁定
        };
    }

    /// <summary>命令 0: Initialize — 初始化电源控制</summary>
    private ResultCode Initialize(IpcRequest request, ref IpcResponse response)
    {
        _state.Initialized = true;
        int eventHandle = unchecked((int)0xFFFF0D20);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, "psc:c: Initialize → KEvent handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: DispatchRequest — 派发电源状态请求</summary>
    private ResultCode DispatchRequest(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 12) return ResultCode.PscResult(2);
        int pmState = BitConverter.ToInt32(request.Data, 0);
        int transitionOrder = BitConverter.ToInt32(request.Data, 4);
        uint flags = BitConverter.ToUInt32(request.Data, 8);
        _state.PmState = pmState;
        Logger.Debug(PortName, $"psc:c: DispatchRequest(state={pmState}, order={transitionOrder}, flags=0x{flags:X8})");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetResult — 获取请求结果 (stub)</summary>
    private ResultCode GetResult(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:c: GetResult (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetState — 获取当前电源状态</summary>
    private ResultCode GetState(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes((uint)_state.PmState));
        Logger.Debug(PortName, $"psc:c: GetState → {_state.PmState}");
        return ResultCode.Success;
    }

    /// <summary>命令 4: Cancel — 取消请求 (stub)</summary>
    private ResultCode Cancel(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:c: Cancel (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 5: PrintModuleInformation — 打印模块信息 (stub)</summary>
    private ResultCode PrintModuleInformation(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:c: PrintModuleInformation (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetModuleInformation — 获取模块信息 (stub)</summary>
    private ResultCode GetModuleInformation(IpcRequest request, ref IpcResponse response)
    {
        // 返回空依赖信息
        response.Data.AddRange(BitConverter.GetBytes(0U)); // DependencyCountBefore
        response.Data.AddRange(BitConverter.GetBytes(0U)); // DependencyCountAfter
        Logger.Debug(PortName, "psc:c: GetModuleInformation → 0 dependencies (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 7: SetRandomDelay — 设置随机延迟 (stub, 17.0.0+)</summary>
    private ResultCode SetRandomDelay(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, "psc:c: SetRandomDelay (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetStateLockUpdateEvent — 获取状态锁更新事件</summary>
    private ResultCode GetStateLockUpdateEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF0D21);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, "psc:c: GetStateLockUpdateEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 11: IsStateLocked — 是否状态已锁定</summary>
    private ResultCode IsStateLocked(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.PscResult(2);
        int pmState = BitConverter.ToInt32(request.Data, 0);
        // 模拟器中不锁定任何状态
        response.Data.AddRange(BitConverter.GetBytes(0U)); // not locked
        Logger.Debug(PortName, $"psc:c: IsStateLocked(state={pmState}) → false");
        return ResultCode.Success;
    }

    internal PscState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}
