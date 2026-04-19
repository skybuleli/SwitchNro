using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

public class MapMemoryTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    // 测试用地址（在进程空间 0x0100_0000 ~ 0x3000_0000 内）
    // 避开 NRO (0x0800_0000)、Stack (0x0200_0000)、TLS (0x0100_0000~0x0100_8000)、Heap (0x2000_0000)
    // SrcBase 和 DstBase 相隔 64KB，避免多页 MapMemory 时重叠
    // 注意: TLS 区域在 StartProcess 中映射为 0x0100_0000~0x0100_8000 (8 页 × 0x200)，
    // MapZero 对已映射页静默跳过，因此测试地址必须在 TLS 区域之后
    private const ulong SrcBase = 0x0100_9000;
    private const ulong DstBase = 0x0101_9000;
    private const ulong PhysBase = 0x0102_9000;
    private const ulong PageSize = 0x1000;

    public MapMemoryTests()
    {
        _system = new HorizonSystem(_memory, _svcDispatcher);
    }

    private HorizonProcess CreateMockProcess()
    {
        var nroModule = new NroModule
        {
            EntryPoint = 0x0800_0000,
            Header = new NroHeader
            {
                Magic = 0,
                Version = 0,
                TextSize = 0x1000,
                RodataSize = 0x1000,
                DataSize = 0x1000,
                BssSize = 0x1000,
            },
            TextSegment = new SegmentInfo(0x0800_0000, 0, 0x1000),
            RodataSegment = new SegmentInfo(0x0801_0000, 0, 0x1000),
            DataSegment = new SegmentInfo(0x0802_0000, 0, 0x1000),
            BssSegment = new SegmentInfo(0x0803_0000, 0, 0x1000),
        };

        _memory.MapZero(nroModule.TextSegment.Address, nroModule.TextSegment.Size, MemoryPermissions.ReadExecute, MemoryType.CodeStatic);
        _memory.MapZero(nroModule.RodataSegment.Address, nroModule.RodataSegment.Size, MemoryPermissions.Read, MemoryType.CodeStatic);
        _memory.MapZero(nroModule.DataSegment.Address, nroModule.DataSegment.Size, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);
        _memory.MapZero(nroModule.BssSegment.Address, nroModule.BssSegment.Size, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);

        var processInfo = new ProcessInfo
        {
            Name = "MapMemoryTest",
            EntryPoint = nroModule.EntryPoint,
        };

        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    // ──────────────────── SVC 0x03 MapMemory ────────────────────

    [Fact]
    public void MapMemory_RemapsSourceToDestination()
    {
        _process = CreateMockProcess();

        // 先映射源地址并写入数据
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(SrcBase, BitConverter.GetBytes(0xDEADBEEFUL));

        // 执行 MapMemory：src → dst
        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        var result = _system.MapMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 目标地址应可读且数据一致
        var readBack = _memory.Read<uint>(DstBase);
        Assert.Equal(0xDEADBEEFUL, readBack);
    }

    [Fact]
    public void MapMemory_SourceUnmappedAfterRemap()
    {
        _process = CreateMockProcess();

        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(SrcBase, BitConverter.GetBytes(0x12345678UL));

        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        _system.MapMemory(svc);

        // 源地址应不再可访问（物理页已移走）
        Assert.Throws<MemoryAccessException>(() => _memory.Read<uint>(SrcBase));
    }

    [Fact]
    public void MapMemory_MultiplePages()
    {
        _process = CreateMockProcess();

        const ulong multiSize = PageSize * 4;
        _memory.MapZero(SrcBase, multiSize, MemoryPermissions.ReadWrite);

        // 写入每页不同的数据
        for (int i = 0; i < 4; i++)
            _memory.Write(SrcBase + (ulong)i * PageSize, BitConverter.GetBytes(i + 100));

        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = multiSize };
        var result = _system.MapMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证所有页数据
        for (int i = 0; i < 4; i++)
        {
            var val = _memory.Read<int>(DstBase + (ulong)i * PageSize);
            Assert.Equal(i + 100, val);
        }
    }

    [Fact]
    public void MapMemory_UnalignedAddress_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x03, X0 = 0x1001, X1 = SrcBase, X2 = PageSize };
        var result = _system.MapMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapMemory_UnalignedSize_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = 0x800 };
        var result = _system.MapMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapMemory_ZeroSize_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = 0 };
        var result = _system.MapMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapMemory_AddressOutOfRange_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        // 地址低于进程空间下限 (0x0100_0000)
        var svc = new SvcInfo { SvcNumber = 0x03, X0 = 0x1000, X1 = SrcBase, X2 = PageSize };
        var result = _system.MapMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapMemory_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        var result = _system.MapMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 0x04 UnmapMemory ────────────────────

    [Fact]
    public void UnmapMemory_ReversesMapMemory()
    {
        _process = CreateMockProcess();

        // 先 MapMemory
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(SrcBase, BitConverter.GetBytes(0xCAFEBABEUL));

        var mapSvc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        _system.MapMemory(mapSvc);

        // 再 UnmapMemory（逆操作）
        var unmapSvc = new SvcInfo { SvcNumber = 0x04, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        var result = _system.UnmapMemory(unmapSvc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 源地址应恢复可读
        var readBack = _memory.Read<uint>(SrcBase);
        Assert.Equal(0xCAFEBABEUL, readBack);
    }

    [Fact]
    public void UnmapMemory_DstUnmappedAfterUnmap()
    {
        _process = CreateMockProcess();

        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        var mapSvc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        _system.MapMemory(mapSvc);

        var unmapSvc = new SvcInfo { SvcNumber = 0x04, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        _system.UnmapMemory(unmapSvc);

        // 目标地址应不再可访问
        Assert.Throws<MemoryAccessException>(() => _memory.Read<uint>(DstBase));
    }

    [Fact]
    public void UnmapMemory_UnalignedAddress_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x04, X0 = 0x2001, X1 = SrcBase, X2 = PageSize };
        var result = _system.UnmapMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    // ──────────────────── SVC 0x35 MapPhysicalMemory ────────────────────

    [Fact]
    public void MapPhysicalMemory_MapsReadWritePages()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = PageSize };
        var result = _system.MapPhysicalMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 应可读写
        _memory.Write(PhysBase, BitConverter.GetBytes(0xABCDEF01UL));
        var readBack = _memory.Read<uint>(PhysBase);
        Assert.Equal(0xABCDEF01UL, readBack);
    }

    [Fact]
    public void MapPhysicalMemory_MultiplePages()
    {
        _process = CreateMockProcess();

        const ulong multiSize = PageSize * 8;
        var svc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = multiSize };
        var result = _system.MapPhysicalMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证每页可写
        for (int i = 0; i < 8; i++)
        {
            var addr = PhysBase + (ulong)i * PageSize;
            _memory.Write(addr, BitConverter.GetBytes(i * 42));
            Assert.Equal(i * 42, _memory.Read<int>(addr));
        }
    }

    [Fact]
    public void MapPhysicalMemory_PagesInitiallyZeroed()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = PageSize };
        _system.MapPhysicalMemory(svc);

        // 映射的页应为零填充
        var buf = new byte[8];
        _memory.Read(PhysBase, buf);
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Fact]
    public void MapPhysicalMemory_UnalignedAddress_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x35, X0 = 0x3001, X2 = PageSize };
        var result = _system.MapPhysicalMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapPhysicalMemory_ZeroSize_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = 0 };
        var result = _system.MapPhysicalMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapPhysicalMemory_OutOfRange_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        // 超出进程空间上限 (0x3000_0000)
        var svc = new SvcInfo { SvcNumber = 0x35, X0 = 0x4000_0000, X2 = PageSize };
        var result = _system.MapPhysicalMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void MapPhysicalMemory_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = PageSize };
        var result = _system.MapPhysicalMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 0x36 UnmapPhysicalMemory ────────────────────

    [Fact]
    public void UnmapPhysicalMemory_UnmapsPreviouslyMappedRegion()
    {
        _process = CreateMockProcess();

        // 先映射
        var mapSvc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = PageSize };
        _system.MapPhysicalMemory(mapSvc);

        // 再取消映射
        var unmapSvc = new SvcInfo { SvcNumber = 0x36, X0 = PhysBase, X2 = PageSize };
        var result = _system.UnmapPhysicalMemory(unmapSvc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 地址应不再可访问
        Assert.Throws<MemoryAccessException>(() => _memory.Read<uint>(PhysBase));
    }

    [Fact]
    public void UnmapPhysicalMemory_ReleasesPhysicalPages()
    {
        _process = CreateMockProcess();

        var residentBefore = _memory.ResidentSize;

        // 映射
        var mapSvc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = PageSize * 4 };
        _system.MapPhysicalMemory(mapSvc);
        var residentAfterMap = _memory.ResidentSize;
        Assert.True(residentAfterMap > residentBefore);

        // 取消映射
        var unmapSvc = new SvcInfo { SvcNumber = 0x36, X0 = PhysBase, X2 = PageSize * 4 };
        _system.UnmapPhysicalMemory(unmapSvc);

        // 常驻内存应回落到映射前的水平
        Assert.True(_memory.ResidentSize < residentAfterMap);
    }

    [Fact]
    public void UnmapPhysicalMemory_UnalignedAddress_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x36, X0 = 0x3001, X2 = PageSize };
        var result = _system.UnmapPhysicalMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    // ──────────────────── MemoryType tagging ────────────────────

    [Fact]
    public void MapMemory_DestinationTaggedAsAlias()
    {
        _process = CreateMockProcess();

        // 映射源地址并写入数据
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite, MemoryType.Heap);
        _memory.Write(SrcBase, BitConverter.GetBytes(0xDEADBEEFUL));

        var svc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        var result = _system.MapMemory(svc);
        Assert.True(result.ReturnCode.IsSuccess);

        // 目标地址的 MemoryType 应为 Alias
        var info = _memory.QueryMemoryInfo(DstBase);
        Assert.Equal((uint)MemoryType.Alias, info.Type);
    }

    [Fact]
    public void UnmapMemory_RestoresOriginalMemoryType()
    {
        _process = CreateMockProcess();

        // 映射源地址，类型为 Heap
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite, MemoryType.Heap);
        _memory.Write(SrcBase, BitConverter.GetBytes(0xCAFEBABEUL));

        // MapMemory: src(Heap) → dst(Alias)
        var mapSvc = new SvcInfo { SvcNumber = 0x03, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        _system.MapMemory(mapSvc);

        // 验证 dst 是 Alias
        Assert.Equal((uint)MemoryType.Alias, _memory.QueryMemoryInfo(DstBase).Type);

        // UnmapMemory: dst(Alias) → src(Heap)
        var unmapSvc = new SvcInfo { SvcNumber = 0x04, X0 = DstBase, X1 = SrcBase, X2 = PageSize };
        _system.UnmapMemory(unmapSvc);

        // 源地址恢复原始 Heap 类型
        var info = _memory.QueryMemoryInfo(SrcBase);
        Assert.Equal((uint)MemoryType.Heap, info.Type);
    }

    [Fact]
    public void MapPhysicalMemory_TaggedAsIoType()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x35, X0 = PhysBase, X2 = PageSize };
        _system.MapPhysicalMemory(svc);

        // 物理内存映射应标记为 Io 类型
        var info = _memory.QueryMemoryInfo(PhysBase);
        Assert.Equal((uint)MemoryType.Io, info.Type);
    }

    // ──────────────────── VirtualMemoryManager Remap/MapAlias/UnmapAlias ────────────────────

    [Fact]
    public void Remap_MovesPhysicalPagesFromSrcToDst()
    {
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(SrcBase, BitConverter.GetBytes(0xBEEFCAFEUL));

        _memory.Remap(SrcBase, DstBase, PageSize, MemoryPermissions.ReadWrite);

        // 目标地址数据一致
        Assert.Equal(0xBEEFCAFEUL, _memory.Read<uint>(DstBase));
        // 源地址不再可访问
        Assert.Throws<MemoryAccessException>(() => _memory.Read<uint>(SrcBase));
    }

    [Fact]
    public void MapAlias_CreatesSharedMapping()
    {
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(SrcBase, BitConverter.GetBytes(0x11111111UL));

        _memory.MapAlias(SrcBase, DstBase, PageSize, MemoryPermissions.ReadWrite);

        // 两个地址应指向同一物理页
        Assert.Equal(0x11111111UL, _memory.Read<uint>(DstBase));
        Assert.Equal(0x11111111UL, _memory.Read<uint>(SrcBase));

        // 通过一个地址写入，另一个应可见（共享物理页）
        _memory.Write(DstBase, BitConverter.GetBytes(0x22222222UL));
        Assert.Equal(0x22222222UL, _memory.Read<uint>(SrcBase));
    }

    [Fact]
    public void UnmapAlias_RemovesMappingWithoutFreeingPhysicalPages()
    {
        _memory.MapZero(SrcBase, PageSize, MemoryPermissions.ReadWrite);
        _memory.MapAlias(SrcBase, DstBase, PageSize, MemoryPermissions.ReadWrite);

        var residentBefore = _memory.ResidentSize;

        _memory.UnmapAlias(DstBase, PageSize);

        // 常驻内存不应减少（物理页仍被 SrcBase 引用）
        Assert.Equal(residentBefore, _memory.ResidentSize);
        // 源地址仍可访问
        Assert.Equal(0UL, _memory.Read<uint>(SrcBase)); // 零填充
        // 目标地址不再可访问
        Assert.Throws<MemoryAccessException>(() => _memory.Read<uint>(DstBase));
    }

    public void Dispose() => _memory.Dispose();
}
