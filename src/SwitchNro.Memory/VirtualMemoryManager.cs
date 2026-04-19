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
    private const int PageSize = 0x1000; // 4KB (虚拟页粒度，保持与 Horizon OS 一致)
    private const int PageBits = 12;
    private const ulong PageMask = PageSize - 1;

    // Apple Silicon 16KB 块大小（物理内存分配粒度，HVF 映射粒度）
    private const ulong ArmBlockSize = 0x4000;
    private const ulong ArmBlockMask = ArmBlockSize - 1;

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
    public void Map(ulong vaddr, ReadOnlySpan<byte> data, MemoryPermissions perms, MemoryType type = MemoryType.Normal)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + (ulong)data.Length);

        // 映射时先赋予写入权限，数据拷贝完成后再切换到目标权限
        var effectivePerms = perms | MemoryPermissions.Write;

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            var page = _physicalAllocator.AllocatePage(addr);
            if (page == IntPtr.Zero)
                throw new MemoryAllocationException("物理内存不足，无法分配新页");

            _pageTable.Map(addr, page, effectivePerms, type);
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
    public void MapZero(ulong vaddr, ulong size, MemoryPermissions perms, MemoryType type = MemoryType.Normal)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + size);

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            if (_pageTable.IsMapped(addr))
                throw new MemoryAlreadyMappedException(addr);

            var page = _physicalAllocator.AllocatePage(addr);
            if (page == IntPtr.Zero)
                throw new MemoryAllocationException("物理内存不足，无法分配新页");

            _pageTable.Map(addr, page, perms, type);
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
                _physicalAllocator.FreePage(addr);
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

    /// <summary>
    /// 映射别名：将源地址的物理页映射到目标地址（不复制数据，共享物理页）
    /// 当前 SVC 层使用 Remap（move 语义），此方法保留供未来别名映射场景使用
    /// </summary>
    public void MapAlias(ulong srcAddr, ulong dstAddr, ulong size, MemoryPermissions perms)
    {
        var alignedSrc = AlignDown(srcAddr);
        var alignedDst = AlignDown(dstAddr);
        var endOffset = AlignUp(srcAddr + size) - alignedSrc;

        for (ulong offset = 0; offset < endOffset; offset += PageSize)
        {
            if (!_pageTable.TryGetValue(alignedSrc + offset, out var physPage, out _))
                throw new MemoryAccessException($"MapAlias: 源页未映射 0x{alignedSrc + offset:X16}");

            _pageTable.Map(alignedDst + offset, physPage, perms);
            _tlb.Invalidate(alignedDst + offset);
        }

        Logger.Info(nameof(VirtualMemoryManager), $"映射别名 0x{dstAddr:X16} ← 0x{srcAddr:X16} 大小=0x{size:X16} [{perms}]");
    }

    /// <summary>
    /// 取消映射别名：仅移除页表映射，不释放底层物理页
    /// 用于 SVC 0x04 UnmapMemory 的实现
    /// </summary>
    public void UnmapAlias(ulong vaddr, ulong size)
    {
        var alignedAddr = AlignDown(vaddr);
        var endAddr = AlignUp(vaddr + size);

        for (ulong addr = alignedAddr; addr < endAddr; addr += PageSize)
        {
            // 仅移除映射，不释放物理页（因为物理页仍被源地址引用）
            _pageTable.Unmap(addr);
            _tlb.Invalidate(addr);
        }

        Logger.Info(nameof(VirtualMemoryManager), $"取消映射别名 0x{vaddr:X16} 大小=0x{size:X16}");
    }

    /// <summary>
    /// 重映射：将源地址的物理页移动到目标地址并更改权限
    /// 源地址被取消映射（不释放物理页），目标地址获得物理页和新权限
    /// 用于 SVC 0x03 MapMemory 的 move 语义实现
    /// </summary>
    public void Remap(ulong srcAddr, ulong dstAddr, ulong size, MemoryPermissions dstPerms, MemoryType dstType = MemoryType.Alias)
    {
        var alignedSrc = AlignDown(srcAddr);
        var alignedDst = AlignDown(dstAddr);
        var endOffset = AlignUp(srcAddr + size) - alignedSrc;

        for (ulong offset = 0; offset < endOffset; offset += PageSize)
        {
            if (!_pageTable.TryGetValue(alignedSrc + offset, out var physPage, out _))
                throw new MemoryAccessException($"Remap: 源页未映射 0x{alignedSrc + offset:X16}");

            // 目标地址获得物理页、新权限和新类型（MapMemory 目标为 Alias）
            _pageTable.Map(alignedDst + offset, physPage, dstPerms, dstType);
            _tlb.Invalidate(alignedDst + offset);

            // 源地址取消映射（不释放物理页，因为已移至目标）
            _pageTable.Unmap(alignedSrc + offset);
            _tlb.Invalidate(alignedSrc + offset);
        }

        Logger.Info(nameof(VirtualMemoryManager), $"重映射 0x{srcAddr:X16} → 0x{dstAddr:X16} 大小=0x{size:X16} [{dstPerms}] type={dstType}");
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

    /// <summary>检查虚拟地址是否已映射（高效检查，无异常）</summary>
    public bool IsMapped(ulong vaddr)
    {
        var pageAddr = AlignDown(vaddr);
        return _pageTable.IsMapped(pageAddr);
    }

    /// <summary>
    /// 查询虚拟地址的内存区域信息（用于 SVC 0x05 QueryMemory）
    /// 从指定地址开始，查找连续的相同类型/权限的页，返回区域信息
    /// </summary>
    public MemoryInfo QueryMemoryInfo(ulong queryAddr)
    {
        var pageAddr = AlignDown(queryAddr);

        // 未映射的地址：查找下一个已映射页的位置
        if (!_pageTable.TryGetValue(pageAddr, out _, out var perms, out var type))
        {
            // 找到第一个已映射的页
            ulong nextMapped = FindNextMappedPage(pageAddr + PageSize);
            return new MemoryInfo
            {
                BaseAddress = pageAddr,
                Size = nextMapped - pageAddr,
                Type = (uint)MemoryType.Unmapped,
                Attribute = 0,
                Permission = 0,
                IpcRefCount = 0
            };
        }

        // 已映射的地址：计算连续相同类型/权限的页的范围
        ulong regionStart = FindRegionStart(pageAddr, perms, type);
        ulong regionEnd = FindRegionEnd(pageAddr, perms, type);

        return new MemoryInfo
        {
            BaseAddress = regionStart,
            Size = regionEnd - regionStart,
            Type = (uint)type,
            Attribute = 0,
            Permission = (uint)perms,
            IpcRefCount = 0
        };
    }

    /// <summary>从指定地址向后查找第一个已映射的页</summary>
    private ulong FindNextMappedPage(ulong startAddr)
    {
        // 限制搜索范围在进程地址空间内（0x0008_0000 ~ 0x3000_0000）
        const ulong SearchLimit = 0x0000_3000_0000UL;
        for (ulong addr = startAddr; addr < SearchLimit; addr += PageSize)
        {
            if (_pageTable.IsMapped(addr)) return addr;
        }
        return SearchLimit;
    }

    /// <summary>向后查找区域起始地址（连续相同类型/权限的页）</summary>
    private ulong FindRegionStart(ulong pageAddr, MemoryPermissions perms, MemoryType type)
    {
        ulong addr = pageAddr;
        while (addr >= PageSize)
        {
            ulong prev = addr - PageSize;
            if (!_pageTable.TryGetValue(prev, out _, out var prevPerms, out var prevType)) break;
            if (prevPerms != perms || prevType != type) break;
            addr = prev;
        }
        return addr;
    }

    /// <summary>向前查找区域结束地址（连续相同类型/权限的页）</summary>
    private ulong FindRegionEnd(ulong pageAddr, MemoryPermissions perms, MemoryType type)
    {
        const ulong SearchLimit = 0x0000_3000_0000UL;
        ulong addr = pageAddr + PageSize;
        while (addr < SearchLimit)
        {
            if (!_pageTable.TryGetValue(addr, out _, out var nextPerms, out var nextType)) break;
            if (nextPerms != perms || nextType != type) break;
            addr += PageSize;
        }
        return addr;
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

    /// <summary>
    /// 获取 16KB 块的 host 基地址和块对齐的 GPA（用于 HVF 映射）
    /// Apple Silicon 要求 hv_vm_map 的 uaddr/gpa/size 均 16KB 对齐
    /// </summary>
    /// <param name="vaddr">任意虚拟地址</param>
    /// <returns>块对齐的 host 基地址 + 块对齐的 GPA，或 (Zero, 0) 如果块不存在</returns>
    public (IntPtr hostBase, ulong blockGpa) GetHvfBlockInfo(ulong vaddr)
    {
        ulong blockAddr = vaddr & ~ArmBlockMask;
        var hostBase = _physicalAllocator.GetBlockBase(blockAddr);
        return (hostBase, blockAddr);
    }

    /// <summary>
    /// 检查指定 16KB 块是否已分配（用于 HVF 映射判断）
    /// </summary>
    public bool IsHvfBlockAllocated(ulong vaddr)
    {
        ulong blockAddr = vaddr & ~ArmBlockMask;
        return _physicalAllocator.IsBlockAllocated(blockAddr);
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

        var newPage = _physicalAllocator.AllocatePage(pageAddr);
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

/// <summary>内存已映射异常 — 尝试映射的页面已被占用</summary>
public sealed class MemoryAlreadyMappedException : Exception
{
    /// <summary>冲突的虚拟地址</summary>
    public ulong Address { get; }

    public MemoryAlreadyMappedException(ulong address)
        : base($"虚拟地址 0x{address:X16} 已经被映射")
    {
        Address = address;
    }
}
