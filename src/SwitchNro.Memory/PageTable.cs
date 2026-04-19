using System;
using System.Collections.Generic;

namespace SwitchNro.Memory;

/// <summary>
/// 两级页表实现
/// 使用 Dictionary 替代固定大小数组以节省内存
/// </summary>
internal sealed class PageTable
{
    private readonly Dictionary<ulong, (IntPtr PhysicalPage, MemoryPermissions Perms, MemoryType Type)> _entries = new();

    public void Map(ulong vaddr, IntPtr physicalPage, MemoryPermissions perms, MemoryType type = MemoryType.Normal)
    {
        _entries[vaddr] = (physicalPage, perms, type);
    }

    public void Unmap(ulong vaddr)
    {
        _entries.Remove(vaddr);
    }

    public bool IsMapped(ulong vaddr) => _entries.ContainsKey(vaddr);

    public bool TryGetValue(ulong vaddr, out IntPtr physicalPage, out MemoryPermissions perms, out MemoryType type)
    {
        if (_entries.TryGetValue(vaddr, out var entry))
        {
            physicalPage = entry.PhysicalPage;
            perms = entry.Perms;
            type = entry.Type;
            return true;
        }
        physicalPage = IntPtr.Zero;
        perms = MemoryPermissions.None;
        type = MemoryType.Unmapped;
        return false;
    }

    // 兼容旧调用：不关心 MemoryType
    public bool TryGetValue(ulong vaddr, out IntPtr physicalPage, out MemoryPermissions perms)
    {
        return TryGetValue(vaddr, out physicalPage, out perms, out _);
    }

    public MemoryPermissions GetPermissions(ulong vaddr)
    {
        return _entries.TryGetValue(vaddr, out var entry) ? entry.Perms : MemoryPermissions.None;
    }

    public MemoryType GetMemoryType(ulong vaddr)
    {
        return _entries.TryGetValue(vaddr, out var entry) ? entry.Type : MemoryType.Unmapped;
    }

    public void UpdatePermissions(ulong vaddr, MemoryPermissions newPerms)
    {
        if (_entries.TryGetValue(vaddr, out var entry))
            _entries[vaddr] = (entry.PhysicalPage, newPerms, entry.Type);
    }

    public void UpdateMemoryType(ulong vaddr, MemoryType newType)
    {
        if (_entries.TryGetValue(vaddr, out var entry))
            _entries[vaddr] = (entry.PhysicalPage, entry.Perms, newType);
    }

    public void Clear() => _entries.Clear();
}
