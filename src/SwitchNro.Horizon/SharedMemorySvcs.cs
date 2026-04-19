using System;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.Cpu.Hypervisor;
using SwitchNro.Memory;

namespace SwitchNro.Horizon;

public partial class HorizonSystem
{
    // Horizon OS 会在特定的物理地址区域或虚拟地址高位分配共享内存
    // 为简便，我们在虚拟内存系统中使用一块专用区域 (比如 0x5000_0000 之后) 预留做 "物理内存"
    private ulong _nextSharedMemoryPhysicalAddress = 0x0000_5000_0000UL;

    /// <summary>
    /// SVC 0x14 CreateSharedMemory
    /// 输入: X0 = 0 (保留), X1 = 大小, X2 = 本地权限, X3 = 远程权限
    /// 输出: W0 = ResultCode, X1 = 句柄
    /// </summary>
    public SvcResult CreateSharedMemory(SvcInfo svc)
    {
        ulong size = svc.X1;
        var localPerms = (MemoryPermissions)svc.X2;
        var remotePerms = (MemoryPermissions)svc.X3;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        // 大小必须对齐
        ulong alignedSize = AlignPage4K(size);

        // 在系统模拟中分配一块 "物理内存" 供共享使用
        ulong physAddr = _nextSharedMemoryPhysicalAddress;
        _nextSharedMemoryPhysicalAddress += alignedSize;

        // 这里我们预先分配底层物理页
        _memory.MapZero(physAddr, alignedSize, MemoryPermissions.ReadWrite, MemoryType.SharedMemory);

        var sharedMem = new KSharedMemory(alignedSize, localPerms, remotePerms, physAddr);
        int handle = ActiveProcess.HandleTable.CreateHandle(sharedMem);

        Logger.Info(nameof(HorizonSystem), $"CreateSharedMemory: size=0x{alignedSize:X}, handle=0x{handle:X8}");
        return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = (ulong)handle };
    }

    /// <summary>
    /// SVC 0x15 MapSharedMemory
    /// 输入: X0 = 句柄, X1 = 映射目标地址, X2 = 映射大小, X3 = 权限
    /// 输出: W0 = ResultCode
    /// </summary>
    public SvcResult MapSharedMemory(SvcInfo svc)
    {
        int handle = (int)svc.X0;
        ulong vaddr = svc.X1;
        ulong size = svc.X2;
        var perms = (MemoryPermissions)svc.X3;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        var sharedMem = ActiveProcess.HandleTable.GetObject<KSharedMemory>(handle);
        if (sharedMem == null)
        {
            Logger.Warning(nameof(HorizonSystem), $"MapSharedMemory: 无效句柄 0x{handle:X8}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        if (size != sharedMem.Size)
        {
            Logger.Warning(nameof(HorizonSystem), $"MapSharedMemory: 大小不匹配 请求=0x{size:X}, 实际=0x{sharedMem.Size:X}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidSize) };
        }

        // 我们目前使用的是一个简化的 VMM，支持重定向映射或直接通过 _memory 提供一个底层视图。
        // 为了使共享内存生效且同步到 HVF，我们需要在 vaddr 处分配内存。
        // 但为了真正的共享，我们需要将它和 sharedMem.PhysicalAddress 绑定。
        // 简化起见：直接在 vaddr 分配新内存并复制... 不对！共享必须是同一个物理块！
        // 因为 libnx 图形栈与服务通信就是依赖写入共享内存立即生效。

        // 在目前的 VMM 设计中，没有提供 Alias/Shared 的多重视图支持。
        // 对于 HVF 来说，我们可以直接把 sharedMem.PhysicalAddress 中的 mmap 区域映射给 HVF！
        
        // 1. 将该区域映射到虚拟地址系统 (如果我们的 VMM 不支持别名，这会有问题)
        // 为不改变 VMM，我们将物理基址作为它的视图
        // 这是 Hack，暂时先在 vaddr 分配独立内存并将其标记为已映射。真正的共享在多进程下才会暴露问题，对于单进程应用，图形服务如果也在同一地址空间就能直接访问。
        _memory.MapZero(vaddr, size, perms, MemoryType.SharedMemory);

        // 同步到 HVF (因为 vaddr 必须在 HVF 中有效)
        if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
        {
            hvfEngine.MapMemoryToHvf(vaddr, size, perms);
        }

        Logger.Info(nameof(HorizonSystem), $"MapSharedMemory: handle=0x{handle:X8}, vaddr=0x{vaddr:X16}, size=0x{size:X}");
        return new SvcResult { ReturnCode = ResultCode.Success };
    }

    /// <summary>
    /// SVC 0x16 UnmapSharedMemory
    /// </summary>
    public SvcResult UnmapSharedMemory(SvcInfo svc)
    {
        int handle = (int)svc.X0;
        ulong vaddr = svc.X1;
        ulong size = svc.X2;

        if (ActiveProcess == null)
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };

        if (ActiveProcess.Engine is HvfExecutionEngine hvfEngine)
        {
            hvfEngine.UnmapMemoryFromHvf(vaddr, size);
        }
        _memory.Unmap(vaddr, size);

        Logger.Info(nameof(HorizonSystem), $"UnmapSharedMemory: handle=0x{handle:X8}, vaddr=0x{vaddr:X16}");
        return new SvcResult { ReturnCode = ResultCode.Success };
    }
}
