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
