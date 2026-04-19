using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

public class SetHeapSizeTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;

    public SetHeapSizeTests()
    {
        _system = new HorizonSystem(_memory, _svcDispatcher);
    }

    /// <summary>创建一个模拟进程（使用 StubEngine 避免 HVF 依赖）</summary>
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

        // 映射 NRO 段内存
        _memory.MapZero(nroModule.TextSegment.Address, nroModule.TextSegment.Size, MemoryPermissions.ReadExecute);
        _memory.MapZero(nroModule.RodataSegment.Address, nroModule.RodataSegment.Size, MemoryPermissions.Read);
        _memory.MapZero(nroModule.DataSegment.Address, nroModule.DataSegment.Size, MemoryPermissions.ReadWrite);
        _memory.MapZero(nroModule.BssSegment.Address, nroModule.BssSegment.Size, MemoryPermissions.ReadWrite);

        var processInfo = new ProcessInfo
        {
            Name = "TestProcess",
            EntryPoint = nroModule.EntryPoint,
        };

        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    [Fact]
    public void SetHeapSize_FirstCall_ReturnsFixedBaseAddress()
    {
        var process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x01, X1 = 0x1000 }; // 请求 4KB 堆
        var result = _system.SetHeapSize(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.HeapBase, result.ReturnValue1);
        Assert.Equal(0x1000UL, process.HeapSize);
        Assert.Equal(HorizonSystem.HeapBase, process.HeapAddress);
    }

    [Fact]
    public void SetHeapSize_AllocatesReadableWritableMemory()
    {
        CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x01, X1 = 0x2000 }; // 8KB
        var result = _system.SetHeapSize(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        ulong heapBase = result.ReturnValue1;

        // 验证堆内存可读写
        _memory.Write(heapBase, 0xDEADBEEFUL);
        var readBack = _memory.Read<ulong>(heapBase);
        Assert.Equal(0xDEADBEEFUL, readBack);

        // 验证堆内初始为零
        var zeroBytes = new byte[8];
        _memory.Read(heapBase + 8, zeroBytes);
        Assert.All(zeroBytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void SetHeapSize_SizeAlignedTo4KB()
    {
        var process = CreateMockProcess();

        // 请求非对齐大小 (100 字节)
        var svc = new SvcInfo { SvcNumber = 0x01, X1 = 100 };
        var result = _system.SetHeapSize(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        // 内部对齐到 4KB (0x1000)
        Assert.Equal(0x1000UL, process.HeapSize);
    }

    [Fact]
    public void SetHeapSize_ExpandHeap_PreservesExistingData()
    {
        var process = CreateMockProcess();

        // 首次分配 4KB
        var svc1 = new SvcInfo { SvcNumber = 0x01, X1 = 0x1000 };
        var result1 = _system.SetHeapSize(svc1);
        ulong heapBase = result1.ReturnValue1;

        // 写入数据
        _memory.Write(heapBase, 0xCAFEBABEUL);

        // 扩展到 8KB
        var svc2 = new SvcInfo { SvcNumber = 0x01, X1 = 0x2000 };
        var result2 = _system.SetHeapSize(svc2);

        Assert.True(result2.ReturnCode.IsSuccess);
        Assert.Equal(heapBase, result2.ReturnValue1); // 基地址不变
        Assert.Equal(0x2000UL, process.HeapSize);

        // 旧数据仍然存在
        var readBack = _memory.Read<ulong>(heapBase);
        Assert.Equal(0xCAFEBABEUL, readBack);
    }

    [Fact]
    public void SetHeapSize_ShrinkHeap_UnmapsExtraPages()
    {
        var process = CreateMockProcess();

        // 首次分配 8KB
        var svc1 = new SvcInfo { SvcNumber = 0x01, X1 = 0x2000 };
        var result1 = _system.SetHeapSize(svc1);
        ulong heapBase = result1.ReturnValue1;

        // 在偏移 0x1000 处写入数据（第二页）
        _memory.Write(heapBase + 0x1000, 0x12345678UL);

        // 缩小到 4KB
        var svc2 = new SvcInfo { SvcNumber = 0x01, X1 = 0x1000 };
        var result2 = _system.SetHeapSize(svc2);

        Assert.True(result2.ReturnCode.IsSuccess);
        Assert.Equal(heapBase, result2.ReturnValue1); // 基地址不变
        Assert.Equal(0x1000UL, process.HeapSize);

        // 第二页应该被取消映射，访问应抛出异常
        Assert.Throws<MemoryAccessException>(() =>
        {
            var buf = new byte[8];
            _memory.Read(heapBase + 0x1000, buf);
        });

        // 第一页数据仍然存在
        var firstPageData = _memory.Read<ulong>(heapBase);
        Assert.Equal(0UL, firstPageData); // 第一页初始为零
    }

    [Fact]
    public void SetHeapSize_SameSize_ReturnsSameAddress()
    {
        var process = CreateMockProcess();

        var svc1 = new SvcInfo { SvcNumber = 0x01, X1 = 0x3000 };
        var result1 = _system.SetHeapSize(svc1);
        ulong heapBase = result1.ReturnValue1;

        // 请求相同大小
        var svc2 = new SvcInfo { SvcNumber = 0x01, X1 = 0x3000 };
        var result2 = _system.SetHeapSize(svc2);

        Assert.True(result2.ReturnCode.IsSuccess);
        Assert.Equal(heapBase, result2.ReturnValue1);
        Assert.Equal(0x3000UL, process.HeapSize);
    }

    [Fact]
    public void SetHeapSize_ExceedsMaxSize_ReturnsError()
    {
        CreateMockProcess();

        // 请求超过最大限制 (256MB + 1)
        var svc = new SvcInfo { SvcNumber = 0x01, X1 = HorizonSystem.HeapMaxSize + 1 };
        var result = _system.SetHeapSize(svc);

        Assert.False(result.ReturnCode.IsSuccess);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void SetHeapSize_ZeroSize_EffectiveZeroBytes()
    {
        var process = CreateMockProcess();

        // 先分配一些堆
        var svc1 = new SvcInfo { SvcNumber = 0x01, X1 = 0x1000 };
        _system.SetHeapSize(svc1);

        // 缩小到零
        var svc2 = new SvcInfo { SvcNumber = 0x01, X1 = 0 };
        var result = _system.SetHeapSize(svc2);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, process.HeapSize);

        // 整个堆区域应被取消映射
        Assert.Throws<MemoryAccessException>(() =>
        {
            var buf = new byte[8];
            _memory.Read(result.ReturnValue1, buf);
        });
    }

    [Fact]
    public void SetHeapSize_FirstCallZeroSize_SetsHeapAddressButUnmapped()
    {
        var process = CreateMockProcess();

        // 首次调用请求大小为 0
        var svc = new SvcInfo { SvcNumber = 0x01, X1 = 0 };
        var result = _system.SetHeapSize(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.HeapBase, process.HeapAddress); // 基地址已设置
        Assert.Equal(0UL, process.HeapSize); // 大小为 0

        // 访问返回的地址应抛出异常（无内存映射）
        Assert.Throws<MemoryAccessException>(() =>
        {
            var buf = new byte[8];
            _memory.Read(result.ReturnValue1, buf);
        });
    }

    [Fact]
    public void SetHeapSize_NoActiveProcess_ReturnsError()
    {
        // 不创建进程，ActiveProcess 为 null
        var svc = new SvcInfo { SvcNumber = 0x01, X1 = 0x1000 };
        var result = _system.SetHeapSize(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    public void Dispose() => _memory.Dispose();
}
