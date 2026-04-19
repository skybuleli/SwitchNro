namespace SwitchNro.Horizon;

/// <summary>
/// SVC 0x29 GetInfo 的信息类型枚举
/// 对应 Horizon OS 内核定义的 InfoType
/// </summary>
public enum InfoType : ulong
{
    /// <summary>允许运行的 CPU 核心掩码 (Handle=Process)</summary>
    CoreMask = 0,

    /// <summary>线程优先级掩码 (Handle=Process)</summary>
    PriorityMask = 1,

    /// <summary>Alias 区域基地址 (Handle=Process)</summary>
    AliasRegionAddress = 2,

    /// <summary>Alias 区域大小 (Handle=Process)</summary>
    AliasRegionSize = 3,

    /// <summary>堆区域基地址 (Handle=Process)</summary>
    HeapRegionAddress = 4,

    /// <summary>堆区域大小 (Handle=Process)</summary>
    HeapRegionSize = 5,

    /// <summary>进程总内存大小 (Handle=Process)</summary>
    TotalMemorySize = 6,

    /// <summary>进程已用内存大小 (Handle=Process)</summary>
    UsedMemorySize = 7,

    /// <summary>调试器是否附加 (Handle=Zero)</summary>
    DebuggerAttached = 8,

    /// <summary>资源限制句柄 (Handle=Zero)</summary>
    ResourceLimit = 9,

    /// <summary>CPU 空闲计数 (Handle=Zero, SubType=coreId 或 -1)</summary>
    IdleTickCount = 10,

    /// <summary>随机熵值 (Handle=Zero, SubType=0-3)</summary>
    RandomEntropy = 11,

    /// <summary>ASLR 区域基地址 (Handle=Process, 2.0.0+)</summary>
    AslrRegionAddress = 12,

    /// <summary>ASLR 区域大小 (Handle=Process, 2.0.0+)</summary>
    AslrRegionSize = 13,

    /// <summary>栈区域基地址 (Handle=Process, 2.0.0+)</summary>
    StackRegionAddress = 14,

    /// <summary>栈区域大小 (Handle=Process)</summary>
    StackRegionSize = 15,

    /// <summary>系统资源总大小 (Handle=Process)</summary>
    SystemResourceSizeTotal = 16,

    /// <summary>系统资源已用大小 (Handle=Process)</summary>
    SystemResourceSizeUsed = 17,

    /// <summary>程序 ID (Handle=Process)</summary>
    ProgramId = 18,

    /// <summary>初始进程 ID 范围 (Handle=Zero, SubType=0 下界, SubType=1 上界)</summary>
    InitialProcessIdRange = 19,

    /// <summary>用户异常上下文地址 (Handle=Process)</summary>
    UserExceptionContextAddress = 20,

    /// <summary>非系统总内存大小 (Handle=Process)</summary>
    TotalNonSystemMemorySize = 21,

    /// <summary>非系统已用内存大小 (Handle=Process)</summary>
    UsedNonSystemMemorySize = 22,

    /// <summary>是否为应用程序 (Handle=Process)</summary>
    IsApplication = 23,
}
