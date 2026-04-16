using System;
using System.Runtime.CompilerServices;
using SwitchNro.Common;
using SwitchNro.Common.Logging;

namespace SwitchNro.Memory;

/// <summary>
/// 虚拟内存管理器
/// 实现按需分页、两级页表和软件 TLB 缓存
/// </summary>
public sealed class VirtualMemoryManager : IDisposable
{
    private const int PageSize = 0x1000; // 4KB
    private const int PageBits = 12;
    private const ulong PageMask = PageSize - 1;

    // 两级页表：PGD (11位) → PUD (9位) → PMD (9位) → PTE
    private readonly PageTable _pageTable;
    private readonly TlbCache _tlb;

    // 物理页分配器
    private readonly PhysicalMemoryAllocator _physicalAllocator;

    /// <summary>当前常驻内存大小（字节）</summary>
    public ulong ResidentSize => _physicalAllocator.AllocatedSize;

    /// <summary>最大常驻内存（字节），默认 3.5GB</summary>
    public ulong MaxResidentSize { get; set; } = 3UL * 1024 * 1024 * 1024 + 512UL * 1024 * 1024;

    public VirtualMemoryManager()
    {
        _pageTable = new PageTable();
        _tlb = new TlbCache();
        _physicalAllocator = new PhysicalMemoryAllocator();
    }

    /// <summary>将数据映射到虚拟地址空间</summary>
    public void Map(ulong vaddr, ReadOnlySpan<byte> data, MemoryPermissions perms)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + (ulong)data.Length);

        // 映射时先赋予写入权限，数据拷贝完成后再切换到目标权限
        var effectivePerms = perms | MemoryPermissions.Write;

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            var page = _physicalAllocator.AllocatePage();
            if (page == IntPtr.Zero)
                throw new MemoryAllocationException("物理内存不足，无法分配新页");

            _pageTable.Map(addr, page, effectivePerms);
            _tlb.Invalidate(addr);
        }

        // 写入数据到映射的页
        Write(vaddr, data);

        // 恢复为目标权限
        if (perms != effectivePerms)
            UpdatePermissions(vaddr, (ulong)data.Length, perms);
        Logger.Info(nameof(VirtualMemoryManager), $"映射虚拟地址 0x{vaddr:X16} - 0x{vaddr + (ulong)data.Length:X16} [{perms}]");
    }

    /// <summary>映射空内存区域（零填充）</summary>
    public void MapZero(ulong vaddr, ulong size, MemoryPermissions perms)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + size);

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            if (_pageTable.IsMapped(addr)) continue;

            var page = _physicalAllocator.AllocatePage();
            if (page == IntPtr.Zero)
                throw new MemoryAllocationException("物理内存不足，无法分配新页");

            _pageTable.Map(addr, page, perms);
            _tlb.Invalidate(addr);
        }

        Logger.Info(nameof(VirtualMemoryManager), $"映射零页 0x{vaddr:X16} - 0x{vaddr + size:X16} [{perms}]");
    }

    /// <summary>取消映射并释放物理页</summary>
    public void Unmap(ulong vaddr, ulong size)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + size);

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            if (_pageTable.TryGetValue(addr, out var page, out _))
            {
                _physicalAllocator.FreePage(page);
                _pageTable.Unmap(addr);
                _tlb.Invalidate(addr);
            }
        }

        Logger.Info(nameof(VirtualMemoryManager), $"取消映射 0x{vaddr:X16} - 0x{vaddr + size:X16}");
    }

    /// <summary>更新虚拟页的权限</summary>
    public void UpdatePermissions(ulong vaddr, ulong size, MemoryPermissions newPerms)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + size);

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            _pageTable.UpdatePermissions(addr, newPerms);
            _tlb.Invalidate(addr);
        }
    }

    /// <summary>读取虚拟内存</summary>
    public void Read(ulong vaddr, Span<byte> destination)
    {
        for (int i = 0; i < destination.Length;)
        {
            var pageAddr = AlignDown(vaddr + (uint)i);
            var pageOffset = (int)((vaddr + (uint)i) - pageAddr);
            var remaining = destination.Length - i;
            var copyLen = Math.Min(remaining, PageSize - pageOffset);

            if (!_tlb.TryGetValue(pageAddr, out var page, out var perms))
            {
                if (!_pageTable.TryGetValue(pageAddr, out page, out perms))
                    throw new MemoryAccessException($"未映射的虚拟地址 0x{pageAddr:X16}");
                _tlb.Set(pageAddr, page, perms);
            }

            if ((perms & MemoryPermissions.Read) == 0)
                throw new MemoryAccessException($"无读取权限: 0x{pageAddr:X16} [{perms}]");

            unsafe
            {
                var src = (byte*)page + pageOffset;
                var dst = (byte*)Unsafe.AsPointer(ref destination[i]);
                Buffer.MemoryCopy(src, dst, copyLen, copyLen);
            }

            i += copyLen;
        }
    }

    /// <summary>写入虚拟内存</summary>
    public void Write(ulong vaddr, ReadOnlySpan<byte> source)
    {
        for (int i = 0; i < source.Length;)
        {
            var pageAddr = AlignDown(vaddr + (uint)i);
            var pageOffset = (int)((vaddr + (uint)i) - pageAddr);
            var remaining = source.Length - i;
            var copyLen = Math.Min(remaining, PageSize - pageOffset);

            if (!_tlb.TryGetValue(pageAddr, out var page, out var perms))
            {
                if (!_pageTable.TryGetValue(pageAddr, out page, out perms))
                    throw new MemoryAccessException($"未映射的虚拟地址 0x{pageAddr:X16}");
                _tlb.Set(pageAddr, page, perms);
            }

            if ((perms & MemoryPermissions.Write) == 0)
                throw new MemoryAccessException($"无写入权限: 0x{pageAddr:X16} [{perms}]");

            unsafe
            {
                var dst = (byte*)page + pageOffset;
                var src = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in source[i]));
                Buffer.MemoryCopy(src, dst, copyLen, copyLen);
            }

            i += copyLen;
        }
    }

    /// <summary>读取一个值</summary>
    public T Read<T>(ulong vaddr) where T : unmanaged
    {
        Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
        Read(vaddr, buffer);
        return Unsafe.ReadUnaligned<T>(ref buffer[0]);
    }

    /// <summary>写入一个值</summary>
    public void Write<T>(ulong vaddr, T value) where T : unmanaged
    {
        Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
        Unsafe.WriteUnaligned(ref buffer[0], value);
        Write(vaddr, buffer);
    }

    /// <summary>获取指针（用于 Hypervisor 直接映射）</summary>
    public IntPtr GetPhysicalAddress(ulong vaddr)
    {
        if (_tlb.TryGetValue(AlignDown(vaddr), out var page, out _))
            return page + (int)(vaddr & PageMask);

        if (_pageTable.TryGetValue(AlignDown(vaddr), out page, out _))
        {
            _tlb.Set(AlignDown(vaddr), page, _pageTable.GetPermissions(AlignDown(vaddr)));
            return page + (int)(vaddr & PageMask);
        }

        return IntPtr.Zero;
    }

    /// <summary>处理缺页中断</summary>
    public void HandlePageFault(ulong vaddr, MemoryPermissions requiredPerms)
    {
        var pageAddr = AlignDown(vaddr);

        if (_pageTable.IsMapped(pageAddr))
        {
            // 页已映射但权限不足
            var currentPerms = _pageTable.GetPermissions(pageAddr);
            if ((currentPerms & requiredPerms) != requiredPerms)
                throw new MemoryAccessException($"缺页: 权限不足 0x{pageAddr:X16} 需要 {requiredPerms} 实际 {currentPerms}");
            return;
        }

        // 按需分配新页
        if (_physicalAllocator.AllocatedSize >= MaxResidentSize)
        {
            Logger.Warning(nameof(VirtualMemoryManager), $"内存压力: 常驻内存接近上限 {_physicalAllocator.AllocatedSize >> 20}MB");
            throw new MemoryAllocationException("常驻内存超出限制");
        }

        var newPage = _physicalAllocator.AllocatePage();
        if (newPage == IntPtr.Zero)
            throw new MemoryAllocationException("物理内存不足");

        _pageTable.Map(pageAddr, newPage, requiredPerms);
        _tlb.Set(pageAddr, newPage, requiredPerms);
        Logger.Debug(nameof(VirtualMemoryManager), $"按需分配页: 0x{pageAddr:X16} [{requiredPerms}]");
    }

    private static ulong AlignDown(ulong addr) => addr & ~PageMask;
    private static ulong AlignUp(ulong addr) => (addr + PageMask) & ~PageMask;

    public void Dispose()
    {
        _physicalAllocator.Dispose();
        _pageTable.Clear();
        _tlb.Clear();
    }
}

/// <summary>内存访问异常</summary>
public sealed class MemoryAccessException : Exception
{
    public MemoryAccessException(string message) : base(message) { }
}

/// <summary>内存分配异常</summary>
public sealed class MemoryAllocationException : Exception
{
    public MemoryAllocationException(string message) : base(message) { }
}
