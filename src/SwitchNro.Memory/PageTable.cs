using System;
using System.Collections.Generic;

namespace SwitchNro.Memory;

/// <summary>
/// 两级页表实现
/// 使用 Dictionary 替代固定大小数组以节省内存
/// </summary>
internal sealed class PageTable
{
    private readonly Dictionary<ulong, (IntPtr PhysicalPage, MemoryPermissions Perms)> _entries = new();

    public void Map(ulong vaddr, IntPtr physicalPage, MemoryPermissions perms)
    {
        _entries[vaddr] = (physicalPage, perms);
    }

    public void Unmap(ulong vaddr)
    {
        _entries.Remove(vaddr);
    }

    public bool IsMapped(ulong vaddr) => _entries.ContainsKey(vaddr);

    public bool TryGetValue(ulong vaddr, out IntPtr physicalPage, out MemoryPermissions perms)
    {
        if (_entries.TryGetValue(vaddr, out var entry))
        {
            physicalPage = entry.PhysicalPage;
            perms = entry.Perms;
            return true;
        }
        physicalPage = IntPtr.Zero;
        perms = MemoryPermissions.None;
        return false;
    }

    public MemoryPermissions GetPermissions(ulong vaddr)
    {
        return _entries.TryGetValue(vaddr, out var entry) ? entry.Perms : MemoryPermissions.None;
    }

    public void UpdatePermissions(ulong vaddr, MemoryPermissions newPerms)
    {
        if (_entries.TryGetValue(vaddr, out var entry))
            _entries[vaddr] = (entry.PhysicalPage, newPerms);
    }

    public void Clear() => _entries.Clear();
}
