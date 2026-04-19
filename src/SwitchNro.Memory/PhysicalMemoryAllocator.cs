using System;
using System.Runtime.InteropServices;

namespace SwitchNro.Memory;

/// <summary>
/// 物理页分配器（16KB 块分配）
/// 
/// Apple Silicon 的 hv_vm_map 要求 host 指针、GPA、size 均 16KB 对齐。
/// 因此物理内存按 16KB 块分配（mmap），每个块包含 4 个 4KB 子页。
/// VMM 仍以 4KB 粒度管理虚拟页面，但物理内存按 16KB 块分配。
/// 
/// 关键优势：
/// - 16KB 块的 host 指针天然 16KB 对齐，可直接传给 hv_vm_map
/// - VMM 和 HVF 共享同一块物理内存，无需数据复制
/// - Guest 写入立即可见（无 VMM/HVF 数据不一致问题）
/// </summary>
internal sealed class PhysicalMemoryAllocator : IDisposable
{
    /// <summary>4KB 子页大小（VMM 虚拟页粒度）</summary>
    private const int SubPageSize = 0x1000;

    /// <summary>16KB 块大小（Apple Silicon 页大小，HVF 映射粒度）</summary>
    private const int BlockSize = 0x4000;

    private const ulong BlockMask = ~(ulong)(BlockSize - 1);

    // mmap 常量
    private const int PROT_READ = 0x01;
    private const int PROT_WRITE = 0x02;
    private const int MAP_PRIVATE = 0x02;
    private const int MAP_ANON = 0x1000; // macOS: MAP_ANON = 0x1000
    private const int MAP_FAILED = -1;

    /// <summary>块跟踪：block-aligned vaddr → (host pointer, ref count)</summary>
    private readonly Dictionary<ulong, (IntPtr HostPtr, int RefCount)> _blocks = new();

    /// <summary>当前已分配的字节数</summary>
    private ulong _allocatedSize;

    /// <summary>当前已分配的字节数</summary>
    public ulong AllocatedSize => _allocatedSize;

    /// <summary>
    /// 分配一个 4KB 子页
    /// 如果所属的 16KB 块已存在，复用该块并增加引用计数
    /// 如果不存在，分配新的 16KB mmap 块
    /// </summary>
    /// <param name="vaddr">虚拟地址（用于确定所属的 16KB 块）</param>
    /// <returns>4KB 子页的 host 指针</returns>
    public unsafe IntPtr AllocatePage(ulong vaddr)
    {
        ulong blockAddr = vaddr & BlockMask;
        int offset = (int)(vaddr & (BlockSize - 1));

        // offset 必须是 4KB 对齐的（VMM 保证传入的 vaddr 已 4KB 对齐）
        if ((offset & (SubPageSize - 1)) != 0)
            offset &= ~(SubPageSize - 1);

        if (_blocks.TryGetValue(blockAddr, out var block))
        {
            // 块已存在，增加引用计数
            _blocks[blockAddr] = (block.HostPtr, block.RefCount + 1);

            // 清零子页（复用场景）
            NativeMemory.Clear((byte*)block.HostPtr + offset, SubPageSize);
            return block.HostPtr + offset;
        }

        // 分配新的 16KB 块
        var hostPtr = mmap(IntPtr.Zero, (UIntPtr)BlockSize,
            PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
        if ((long)hostPtr == MAP_FAILED)
            return IntPtr.Zero;

        // mmap 返回的内存已零初始化（MAP_ANON 保证）
        // 但只使用 offset 处的 4KB 子页，其余保持零
        _blocks[blockAddr] = (hostPtr, 1);
        _allocatedSize += BlockSize;
        return hostPtr + offset;
    }

    /// <summary>
    /// 释放一个 4KB 子页
    /// 减少所属块的引用计数，当计数归零时释放整个 16KB 块
    /// </summary>
    public void FreePage(ulong vaddr)
    {
        ulong blockAddr = vaddr & BlockMask;

        if (_blocks.TryGetValue(blockAddr, out var block))
        {
            int newRefCount = block.RefCount - 1;
            if (newRefCount <= 0)
            {
                // 所有子页已释放，释放整个 16KB 块
                _ = munmap(block.HostPtr, (UIntPtr)BlockSize);
                _blocks.Remove(blockAddr);
                _allocatedSize -= BlockSize;
            }
            else
            {
                _blocks[blockAddr] = (block.HostPtr, newRefCount);
            }
        }
    }

    /// <summary>
    /// 获取 16KB 块的 host 基地址（用于 HVF 映射）
    /// </summary>
    /// <param name="vaddr">虚拟地址（任意 4KB 子页地址即可）</param>
    /// <returns>16KB 块的 host 基地址，如果块不存在返回 IntPtr.Zero</returns>
    public IntPtr GetBlockBase(ulong vaddr)
    {
        ulong blockAddr = vaddr & BlockMask;
        return _blocks.TryGetValue(blockAddr, out var block) ? block.HostPtr : IntPtr.Zero;
    }

    /// <summary>
    /// 检查指定 16KB 块是否存在
    /// </summary>
    public bool IsBlockAllocated(ulong vaddr)
    {
        ulong blockAddr = vaddr & BlockMask;
        return _blocks.ContainsKey(blockAddr);
    }

    /// <summary>
    /// 获取所有已分配的 16KB 块地址（用于 HVF 映射迭代）
    /// </summary>
    public IEnumerable<ulong> GetAllocatedBlockAddresses() => _blocks.Keys;

    public unsafe void Dispose()
    {
        foreach (var (blockAddr, block) in _blocks)
        {
            _ = munmap(block.HostPtr, (UIntPtr)BlockSize);
        }
        _blocks.Clear();
        _allocatedSize = 0;
    }

    // ──────────────────── P/Invoke: mmap / munmap ────────────────────

    [DllImport("libc", EntryPoint = "mmap")]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, long offset);

    [DllImport("libc", EntryPoint = "munmap")]
    private static extern int munmap(IntPtr addr, UIntPtr length);
}
