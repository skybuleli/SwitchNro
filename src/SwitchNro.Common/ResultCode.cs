using System;

namespace SwitchNro.Common;

/// <summary>
/// Horizon OS 结果码，格式为 Module(9) + Description(13)
/// 与 Switch 系统的 Result 布局一致
/// </summary>
public readonly struct ResultCode : IEquatable<ResultCode>
{
    private readonly int _value;

    public ResultCode(int module, int description)
    {
        _value = (module << 9) | description;
    }

    public int Module => (_value >> 9) & 0x1FF;
    public int Description => _value & 0x1FFF;
    public bool IsSuccess => _value == 0;

    public static ResultCode Success => new(0, 0);

    // 常用错误码
    public static ResultCode KernelResult(TKernelResult result) => new(1, (int)result);
    public static ResultCode FsResult(int description) => new(2, description);
    public static ResultCode HtcResult(int description) => new(4, description);
    public static ResultCode SfResult(int description) => new(10, description);
    public static ResultCode PmResult(int description) => new(15, description); // Process Manager module

    public override string ToString() => IsSuccess ? "Success" : $"0x{_value:X8} (Module={Module}, Desc={Description})";
    public bool Equals(ResultCode other) => _value == other._value;
    public override bool Equals(object? obj) => obj is ResultCode other && Equals(other);
    public override int GetHashCode() => _value;
    public static bool operator ==(ResultCode left, ResultCode right) => left.Equals(right);
    public static bool operator !=(ResultCode left, ResultCode right) => !left.Equals(right);
}

/// <summary>内核结果描述</summary>
public enum TKernelResult
{
    Success = 0,
    SessionClosed = 16,
    InvalidState = 42,
    NotImplemented = 518,
    NotSupported = 519,
    InvalidHandle = 602,
    InvalidSize = 604,
    InvalidAddress = 605,
    OutOfResource = 614,
    OutOfMemory = 615,
    LimitReached = 617,
}
