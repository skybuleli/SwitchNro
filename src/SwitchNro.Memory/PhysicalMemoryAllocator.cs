using System;
using System.Runtime.InteropServices;

namespace SwitchNro.Memory;

/// <summary>
/// 物理页分配器
/// 使用 NativeMemory 管理 4KB 非托管物理页
/// </summary>
internal sealed class PhysicalMemoryAllocator : IDisposable
{
    private const int PageSize = 0x1000;

    private readonly List<IntPtr> _allocatedPages = new();
    private readonly HashSet<IntPtr> _freePages = new();
    private ulong _allocatedSize;

    /// <summary>当前已分配的字节数</summary>
    public ulong AllocatedSize => _allocatedSize;

    /// <summary>分配一个 4KB 物理页</summary>
    public unsafe IntPtr AllocatePage()
    {
        // 优先复用已释放的页
        if (_freePages.Count > 0)
        {
            var page = _freePages.First();
            _freePages.Remove(page);
            // 清零
            NativeMemory.Clear((void*)page, PageSize);
            return page;
        }

        // 分配新页
        var newPage = (IntPtr)NativeMemory.AlignedAlloc(PageSize, PageSize);
        if (newPage == IntPtr.Zero)
            return IntPtr.Zero;

        NativeMemory.Clear((void*)newPage, PageSize);
        _allocatedPages.Add(newPage);
        _allocatedSize += PageSize;
        return newPage;
    }

    /// <summary>释放物理页（回收到空闲列表）</summary>
    public void FreePage(IntPtr page)
    {
        if (_allocatedPages.Contains(page))
        {
            _freePages.Add(page);
            _allocatedSize -= PageSize;
        }
    }

    public unsafe void Dispose()
    {
        foreach (var page in _allocatedPages)
        {
            NativeMemory.AlignedFree((void*)page);
        }
        _allocatedPages.Clear();
        _freePages.Clear();
        _allocatedSize = 0;
    }
}
