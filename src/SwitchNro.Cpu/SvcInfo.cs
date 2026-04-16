namespace SwitchNro.Cpu;

/// <summary>SVC 系统调用信息</summary>
public readonly struct SvcInfo
{
    /// <summary>SVC 编号</summary>
    public uint SvcNumber { get; init; }

    /// <summary>系统调用参数（来自 X0-X7 寄存器）</summary>
    public ulong X0 { get; init; }
    public ulong X1 { get; init; }
    public ulong X2 { get; init; }
    public ulong X3 { get; init; }
    public ulong X4 { get; init; }
    public ulong X5 { get; init; }
    public ulong X6 { get; init; }
    public ulong X7 { get; init; }

    /// <summary>当前程序计数器</summary>
    public ulong PC { get; init; }

    /// <summary>当前栈指针</summary>
    public ulong SP { get; init; }

    public override string ToString() => $"SVC 0x{SvcNumber:X2} (X0=0x{X0:X16}, X1=0x{X1:X16})";
}
