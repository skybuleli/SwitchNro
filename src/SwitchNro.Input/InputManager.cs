using System;
using System.Collections.Generic;
using SwitchNro.Common.Logging;

namespace SwitchNro.Input;

/// <summary>
/// 输入管理器
/// 统一管理键盘、鼠标和蓝牙手柄输入
/// </summary>
public sealed class InputManager
{
    /// <summary>当前手柄按键状态</summary>
    public SwitchButtonState ButtonState { get; private set; }

    /// <summary>左摇杆状态</summary>
    public StickState LeftStick { get; private set; }

    /// <summary>右摇杆状态</summary>
    public StickState RightStick { get; private set; }

    /// <summary>触屏状态</summary>
    public TouchState Touch { get; private set; }

    /// <summary>按键映射表</summary>
    private readonly Dictionary<string, SwitchButton> _keyboardMapping = new();

    public InputManager()
    {
        InitializeDefaultMapping();
        Logger.Info(nameof(InputManager), "输入管理器初始化完成");
    }

    /// <summary>处理键盘按键按下</summary>
    public void OnKeyDown(string keyName)
    {
        if (_keyboardMapping.TryGetValue(keyName, out var button))
        {
            ButtonState = ButtonState with
            {
                RawButtons = ButtonState.RawButtons | (1u << (int)button)
            };
        }
    }

    /// <summary>处理键盘按键释放</summary>
    public void OnKeyUp(string keyName)
    {
        if (_keyboardMapping.TryGetValue(keyName, out var button))
        {
            ButtonState = ButtonState with
            {
                RawButtons = ButtonState.RawButtons & ~(1u << (int)button)
            };
        }
    }

    /// <summary>更新摇杆状态</summary>
    public void UpdateStick(int stickIndex, float x, float y)
    {
        if (stickIndex == 0)
            LeftStick = new StickState { X = ClampAxis(x), Y = ClampAxis(y) };
        else
            RightStick = new StickState { X = ClampAxis(x), Y = ClampAxis(y) };
    }

    /// <summary>更新触屏状态</summary>
    public void UpdateTouch(float x, float y, bool touched)
    {
        Touch = new TouchState { X = x, Y = y, IsTouched = touched };
    }

    /// <summary>自定义按键映射</summary>
    public void SetKeyMapping(string keyName, SwitchButton button)
    {
        _keyboardMapping[keyName] = button;
    }

    private void InitializeDefaultMapping()
    {
        // 默认键盘映射（与规格一致）
        _keyboardMapping["X"] = SwitchButton.A;
        _keyboardMapping["Z"] = SwitchButton.B;
        _keyboardMapping["S"] = SwitchButton.X;
        _keyboardMapping["A"] = SwitchButton.Y;
        _keyboardMapping["Q"] = SwitchButton.L;
        _keyboardMapping["E"] = SwitchButton.R;
        _keyboardMapping["1"] = SwitchButton.ZL;
        _keyboardMapping["3"] = SwitchButton.ZR;
        _keyboardMapping["Enter"] = SwitchButton.Plus;
        _keyboardMapping["Backspace"] = SwitchButton.Minus;
        _keyboardMapping["Up"] = SwitchButton.DUp;
        _keyboardMapping["Down"] = SwitchButton.DDown;
        _keyboardMapping["Left"] = SwitchButton.DLeft;
        _keyboardMapping["Right"] = SwitchButton.DRight;
        _keyboardMapping["Escape"] = SwitchButton.Home;
        _keyboardMapping["F12"] = SwitchButton.Screenshot;
    }

    private static float ClampAxis(float value) => Math.Clamp(value, -1.0f, 1.0f);
}

/// <summary>Switch 手柄按键状态</summary>
public readonly struct SwitchButtonState
{
    public uint RawButtons { get; init; }

    public bool IsPressed(SwitchButton button) => (RawButtons & (1u << (int)button)) != 0;
}

/// <summary>Switch 按键枚举</summary>
public enum SwitchButton
{
    A = 0,
    B = 1,
    X = 2,
    Y = 3,
    L = 4,
    R = 5,
    ZL = 6,
    ZR = 7,
    Plus = 8,
    Minus = 9,
    DLeft = 10,
    DUp = 11,
    DRight = 12,
    DDown = 13,
    LStick = 14,
    RStick = 15,
    Home = 16,
    Screenshot = 17,
}

/// <summary>摇杆状态</summary>
public readonly struct StickState
{
    public float X { get; init; }
    public float Y { get; init; }
}

/// <summary>触屏状态</summary>
public readonly struct TouchState
{
    public float X { get; init; }
    public float Y { get; init; }
    public bool IsTouched { get; init; }
}
