using System;
using System.Runtime.CompilerServices;

namespace SwitchNro.Memory;

/// <summary>
/// 软件 TLB 缓存
/// 直接映射式缓存，使用虚拟地址低位做索引
/// </summary>
internal sealed class TlbCache
{
    private const int TlbSize = 4096; // 必须是 2 的幂
    private const int TlbMask = TlbSize - 1;

    private readonly struct TlbEntry
    {
        public readonly ulong VirtualPage;
        public readonly IntPtr PhysicalPage;
        public readonly MemoryPermissions Perms;

        public TlbEntry(ulong vpage, IntPtr ppage, MemoryPermissions perms)
        {
            VirtualPage = vpage;
            PhysicalPage = ppage;
            Perms = perms;
        }
    }

    private readonly TlbEntry[] _entries = new TlbEntry[TlbSize];

    public bool TryGetValue(ulong vaddr, out IntPtr physicalPage, out MemoryPermissions perms)
    {
        ref readonly var entry = ref _entries[(int)(vaddr >> 12) & TlbMask];
        if (entry.VirtualPage == vaddr)
        {
            physicalPage = entry.PhysicalPage;
            perms = entry.Perms;
            return true;
        }
        physicalPage = IntPtr.Zero;
        perms = MemoryPermissions.None;
        return false;
    }

    public void Set(ulong vaddr, IntPtr physicalPage, MemoryPermissions perms)
    {
        _entries[(int)(vaddr >> 12) & TlbMask] = new TlbEntry(vaddr, physicalPage, perms);
    }

    public void Invalidate(ulong vaddr)
    {
        var idx = (int)(vaddr >> 12) & TlbMask;
        if (_entries[idx].VirtualPage == vaddr)
            _entries[idx] = default;
    }

    public void Clear()
    {
        Array.Clear(_entries);
    }
}
