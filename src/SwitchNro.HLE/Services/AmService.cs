using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// appletOE — Applet Manager (普通应用)
/// 核心必选服务 - 管理 Applet 生命周期、窗口焦点、HOME 菜单交互
/// Homebrew 作为 "Library Applet" 运行，需要此服务获取前台状态
/// </summary>
public sealed class AmService : IIpcService
{
    public string PortName => "appletOE";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>当前 Applet 状态</summary>
    private AppletFocusState _focusState = AppletFocusState.InFocus;

    /// <summary>当前操作模式</summary>
    private OperationMode _operationMode = OperationMode.Handheld;

    public AmService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = OpenDefaultApplet,          // 打开默认 Applet
            [1]  = SetScreenShotPermission,     // 设置截图权限
            [10] = SetOperationModeChangedNotification,
            [20] = InitializeApplicationCopyright,
            [22] = SetApplicationCopyrightImage,
            [40] = NotifyRunning,              // 通知应用正在运行
            [50] = GetAppletResourceUserId,    // 获取资源用户 ID
            [60] = SetAppletWindowFocus,       // 设置窗口焦点
            [70] = SetOutOfFocusSuspendingEnabled,
            [80] = GetFocusState,              // 获取焦点状态
            [90] = GetOperationMode,           // 获取操作模式
            [91] = GetPerformanceMode,         // 获取性能模式
            [100] = GetDisplayLogicalResolution,
        };
    }

    /// <summary>命令 0: OpenDefaultApplet — 获取默认 Applet 代理</summary>
    private ResultCode OpenDefaultApplet(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(AmService), "appletOE: OpenDefaultApplet");
        return ResultCode.Success;
    }

    /// <summary>命令 1: SetScreenShotPermission — 设置截图权限</summary>
    private ResultCode SetScreenShotPermission(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: SetScreenShotPermission");
        return ResultCode.Success;
    }

    /// <summary>命令 10: SetOperationModeChangedNotification</summary>
    private ResultCode SetOperationModeChangedNotification(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: SetOperationModeChangedNotification");
        return ResultCode.Success;
    }

    /// <summary>命令 20: InitializeApplicationCopyright</summary>
    private ResultCode InitializeApplicationCopyright(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: InitializeApplicationCopyright");
        return ResultCode.Success;
    }

    /// <summary>命令 22: SetApplicationCopyrightImage</summary>
    private ResultCode SetApplicationCopyrightImage(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: SetApplicationCopyrightImage");
        return ResultCode.Success;
    }

    /// <summary>命令 40: NotifyRunning — 通知应用正在运行</summary>
    private ResultCode NotifyRunning(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: NotifyRunning");
        return ResultCode.Success;
    }

    /// <summary>命令 50: GetAppletResourceUserId — 获取资源用户 ID</summary>
    private ResultCode GetAppletResourceUserId(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: GetAppletResourceUserId");
        // 返回虚拟 Applet Resource User ID
        response.Data.AddRange(BitConverter.GetBytes(0x1000UL));
        return ResultCode.Success;
    }

    /// <summary>命令 60: SetAppletWindowFocus — 设置窗口焦点</summary>
    private ResultCode SetAppletWindowFocus(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length >= 1)
        {
            _focusState = (AppletFocusState)request.Data[0];
            Logger.Info(nameof(AmService), $"appletOE: SetAppletWindowFocus → {_focusState}");
        }
        return ResultCode.Success;
    }

    /// <summary>命令 70: SetOutOfFocusSuspendingEnabled</summary>
    private ResultCode SetOutOfFocusSuspendingEnabled(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), "appletOE: SetOutOfFocusSuspendingEnabled");
        return ResultCode.Success;
    }

    /// <summary>命令 80: GetFocusState — 获取焦点状态</summary>
    private ResultCode GetFocusState(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), $"appletOE: GetFocusState → {_focusState}");
        response.Data.Add((byte)_focusState);
        return ResultCode.Success;
    }

    /// <summary>命令 90: GetOperationMode — 获取操作模式</summary>
    private ResultCode GetOperationMode(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AmService), $"appletOE: GetOperationMode → {_operationMode}");
        response.Data.Add((byte)_operationMode);
        return ResultCode.Success;
    }

    /// <summary>命令 91: GetPerformanceMode — 获取性能模式</summary>
    private ResultCode GetPerformanceMode(IpcRequest request, ref IpcResponse response)
    {
        // 性能模式与操作模式一致
        response.Data.Add((byte)_operationMode);
        return ResultCode.Success;
    }

    /// <summary>命令 100: GetDisplayLogicalResolution</summary>
    private ResultCode GetDisplayLogicalResolution(IpcRequest request, ref IpcResponse response)
    {
        // 返回 1280x720 (手持模式标准分辨率)
        response.Data.AddRange(BitConverter.GetBytes(1280)); // Width
        response.Data.AddRange(BitConverter.GetBytes(720));  // Height
        return ResultCode.Success;
    }

    /// <summary>设置操作模式（外部调用）</summary>
    public void SetOperationMode(OperationMode mode)
    {
        _operationMode = mode;
        Logger.Info(nameof(AmService), $"操作模式变更: {mode}");
    }

    public void Dispose() { }
}

/// <summary>
/// appletAE — Applet Manager (系统应用)
/// 用于系统设置等系统级 Applet
/// </summary>
public sealed class AppletAeService : IIpcService
{
    public string PortName => "appletAE";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public AppletAeService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = OpenSystemAppletProxy,     // 打开系统 Applet 代理
            [100] = GetAppletResourceUserId,
        };
    }

    private ResultCode OpenSystemAppletProxy(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(AppletAeService), "appletAE: OpenSystemAppletProxy");
        return ResultCode.Success;
    }

    private ResultCode GetAppletResourceUserId(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0x1000UL));
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>Applet 焦点状态</summary>
internal enum AppletFocusState : byte
{
    OutOfFocus = 0,
    InFocus = 1,
    Background = 2,
}

/// <summary>操作模式</summary>
public enum OperationMode : byte
{
    Handheld = 0,
    Docked = 1,
}
