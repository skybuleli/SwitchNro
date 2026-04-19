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

    /// <summary>原始数值（用于序列化到 IPC 缓冲区）</summary>
    public int Value => _value;

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
    public static ResultCode LdrResult(int description) => new(9, description);  // Loader module
    public static ResultCode LmResult(int description) => new(7, description);   // Log Manager module
    public static ResultCode ArpResult(int description) => new(29, description); // ARP module (glue)
    public static ResultCode SslResult(int description) => new(22, description); // SSL module
    public static ResultCode NifmResult(int description) => new(31, description); // NIFM module
    public static ResultCode AccResult(int description) => new(5, description);   // Account module
    public static ResultCode PctlResult(int description) => new(25, description); // Parental Control module
    public static ResultCode FriendResult(int description) => new(11, description); // Friend module
    public static ResultCode NsResult(int description) => new(16, description);     // NS (Application Manager) module
    public static ResultCode BcatResult(int description) => new(42, description);  // BCAT module
    public static ResultCode NewsResult(int description) => new(33, description); // News module
    public static ResultCode MmResult(int description) => new(39, description);     // MM (Memory Monitor) module
    public static ResultCode PscResult(int description) => new(35, description);   // PSC (Power State Control) module
    public static ResultCode FatalResult(int description) => new(168, description); // Fatal module
    public static ResultCode ApmResult(int description) => new(17, description);  // APM (Application Performance Management) module
    public static ResultCode RoResult(int description) => new(21, description);   // RO (Relocatable Object) module
    public static ResultCode AudRenResult(int description) => new(129, description); // Audio Renderer module
    public static ResultCode PlResult(int description) => new(27, description);    // PL (Profile Loader / Shared Font) module
    public static ResultCode SplResult(int description) => new(24, description);   // SPL (Secure Platform Layer) module

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
    TimedOut = 22,
    InvalidState = 42,
    // WaitSynchronization 专用错误码
    WaitSyncInvalidHandle = 57,
    WaitSyncCancelled = 59, // SVC 0x0E CancelSynchronization 设置取消标志后，WaitSynchronization 返回此值
    WaitSyncTooManyHandles = 114,
    // ArbitrateLock / WaitProcessWideKeyAtomic 专用错误码
    ConcurrentConflict = 103,
    InvalidCount = 112,
    // CreateThread 专用错误码
    InvalidPriority = 101,
    InvalidProcessorId = 102,
    // SetThreadActivity 专用错误码
    InvalidThreadActivity = 100,
    // 通用错误码
    InvalidHandle = 602,
    InvalidSize = 604,
    InvalidAddress = 605,
    OutOfResource = 614,
    OutOfMemory = 615,
    LimitReached = 617,
    NotImplemented = 518,
    NotSupported = 519,
}
