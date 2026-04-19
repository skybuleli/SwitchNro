using System;

namespace SwitchNro.Memory;

/// <summary>内存访问权限标志（与 Horizon OS 定义一致）</summary>
[Flags]
public enum MemoryPermissions : byte
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Execute = 1 << 2,
    ReadWrite = Read | Write,
    ReadExecute = Read | Execute,
    All = Read | Write | Execute,
}

/// <summary>内存类型（与 Horizon OS MemoryType 一致，用于 QueryMemory）</summary>
public enum MemoryType : ulong
{
    Unmapped = 0,
    Io = 1,
    Normal = 2,
    CodeStatic = 3,       // 代码段（.text, 只读）
    CodeMutable = 4,      // 数据段（.data/.bss, 可写）
    Heap = 5,             // 堆（SetHeapSize）
    SharedMemory = 6,
    Alias = 7,            // MapMemory 目标区域
    ModuleCodeStatic = 8,
    ModuleCodeMutable = 9,
    Ipc = 10,
    Stack = 11,           // 栈
    TransferMemory = 12,
    ThreadLocal = 13,      // TLS
    SharedCode = 14,
    WeiredMapped = 15,
}

/// <summary>内存属性标志（与 Horizon OS MemoryAttribute 一致）</summary>
/// <remarks>当前未使用，保留供未来 QueryMemory Attribute 字段查询使用</remarks>
[Flags]
public enum MemAttrs : uint
{
    None = 0,
    Locked = 1U << 0,
    IpcLocked = 1U << 1,
    DeviceMapped = 1U << 2,
    Uncached = 1U << 3,
}

/// <summary>
/// QueryMemory 返回的内存信息结构体（40 字节）
/// 与 Horizon OS MemoryInfo 布局一致：
///   +0x00: BaseAddress (u64)
///   +0x08: Size (u64)
///   +0x10: Type (u32)
///   +0x14: Attribute (u32)
///   +0x18: Permission (u32)
///   +0x1C: IpcRefCount (u32)
///   +0x20: DeviceRefCount (u32)
///   +0x24: Padding (u32) — 对齐到 8 字节边界
/// </summary>
public struct MemoryInfo
{
    /// <summary>区域基地址</summary>
    public ulong BaseAddress { get; set; }
    /// <summary>区域大小</summary>
    public ulong Size { get; set; }
    /// <summary>内存类型 (MemoryType)</summary>
    public uint Type { get; set; }
    /// <summary>内存属性 (MemAttrs)</summary>
    public uint Attribute { get; set; }
    /// <summary>权限 (MemoryPermissions)</summary>
    public uint Permission { get; set; }
    /// <summary>IPC 引用计数</summary>
    public uint IpcRefCount { get; set; }
    /// <summary>设备引用计数</summary>
    public uint DeviceRefCount { get; set; }
}
