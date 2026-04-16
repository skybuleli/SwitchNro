using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// hid: 输入设备服务 (Human Interface Device)
/// 核心必选 - 管理手柄/触屏输入状态
/// </summary>
public sealed class HidService : IIpcService
{
    public string PortName => "hid:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>手柄输入状态（共享内存结构）</summary>
    private HidSharedMemory _sharedMemory = new();

    public HidService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateAppletResource,  // 创建 Applet 资源
            [1] = ActivateDebugPad,      // 激活调试手柄
            [11] = ActivateTouchScreen,  // 激活触屏
            [66] = StartSixAxisSensor,   // 启动六轴传感器
            [100] = SetSupportedNpadStyleSet, // 设置支持的手柄类型
            [101] = GetSupportedNpadStyleSet,
            [102] = SetSupportedNpadIdType,   // 设置支持的手柄 ID
            [120] = SetNpadJoyHoldType,       // 设置 Joy-Con 握持类型
            [203] = CreateActiveVibrationDevice,
        };
    }

    private ResultCode CreateAppletResource(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(HidService), "hid: CreateAppletResource");
        return ResultCode.Success;
    }

    private ResultCode ActivateDebugPad(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(HidService), "hid: ActivateDebugPad");
        return ResultCode.Success;
    }

    private ResultCode ActivateTouchScreen(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(HidService), "hid: ActivateTouchScreen");
        return ResultCode.Success;
    }

    private ResultCode StartSixAxisSensor(IpcRequest request, ref IpcResponse response)
    {
        return ResultCode.Success;
    }

    private ResultCode SetSupportedNpadStyleSet(IpcRequest request, ref IpcResponse response)
    {
        return ResultCode.Success;
    }

    private ResultCode GetSupportedNpadStyleSet(IpcRequest request, ref IpcResponse response)
    {
        return ResultCode.Success;
    }

    private ResultCode SetSupportedNpadIdType(IpcRequest request, ref IpcResponse response)
    {
        return ResultCode.Success;
    }

    private ResultCode SetNpadJoyHoldType(IpcRequest request, ref IpcResponse response)
    {
        return ResultCode.Success;
    }

    private ResultCode CreateActiveVibrationDevice(IpcRequest request, ref IpcResponse response)
    {
        return ResultCode.Success;
    }

    /// <summary>更新手柄按键状态</summary>
    public void UpdateButtonState(uint buttons)
    {
        _sharedMemory.ControllerState.Buttons = buttons;
    }

    /// <summary>更新摇杆位置</summary>
    public void UpdateStickPosition(int stickIndex, float x, float y)
    {
        if (stickIndex == 0)
        {
            _sharedMemory.ControllerState.LeftStickX = x;
            _sharedMemory.ControllerState.LeftStickY = y;
        }
        else
        {
            _sharedMemory.ControllerState.RightStickX = x;
            _sharedMemory.ControllerState.RightStickY = y;
        }
    }

    /// <summary>更新触屏状态</summary>
    public void UpdateTouchState(int touchIndex, float x, float y, bool touched)
    {
        if (touchIndex < 16)
        {
            _sharedMemory.TouchStates[touchIndex] = new TouchState
            {
                X = (int)x,
                Y = (int)y,
                IsTouched = touched,
            };
        }
    }

    public void Dispose() { }
}

/// <summary>HID 共享内存结构</summary>
internal sealed class HidSharedMemory
{
    public ControllerState ControllerState = new();
    public TouchState[] TouchStates = new TouchState[16];
}

internal sealed class ControllerState
{
    public uint Buttons;
    public float LeftStickX, LeftStickY;
    public float RightStickX, RightStickY;
}

internal struct TouchState
{
    public int X, Y;
    public bool IsTouched;
}
