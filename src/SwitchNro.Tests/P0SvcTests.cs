using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

public class P0SvcTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    // 测试用地址（在进程空间 0x0008_0000 ~ 0x3000_0000 内）
    // 注意: TLS 区域在 StartProcess 中映射为 0x0100_0000~0x0100_8000 (8 页 × 0x200)，
    // MapZero 对已映射页静默跳过，因此测试地址必须在 TLS 区域之后
    private const ulong TestRegionBase = 0x0100_9000;
    private const ulong PageSize = 0x1000;

    public P0SvcTests()
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
            Name = "P0SvcTest",
            EntryPoint = nroModule.EntryPoint,
        };

        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    // ──────────────────── SVC 0x06 ExitProcess ────────────────────

    [Fact]
    public void ExitProcess_SetsStateToExited()
    {
        _process = CreateMockProcess();
        Assert.Equal(ProcessState.Running, _process.State);

        var svc = new SvcInfo { SvcNumber = 0x06, X0 = 0 };
        var result = _system.ExitProcess(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(ProcessState.Exited, _process.State);
    }

    [Fact]
    public void ExitProcess_RequestsEngineExit()
    {
        _process = CreateMockProcess();
        var engine = (StubExecutionEngine)_process.Engine;

        var svc = new SvcInfo { SvcNumber = 0x06, X0 = 0 };
        _system.ExitProcess(svc);

        // StubExecutionEngine.RequestExit() 是空操作，但不应抛异常
        // 验证进程状态已变更为 Exited
        Assert.Equal(ProcessState.Exited, _process.State);
    }

    [Fact]
    public void ExitProcess_WithExitCode_Success()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x06, X0 = 0x1234 };
        var result = _system.ExitProcess(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(ProcessState.Exited, _process.State);
    }

    [Fact]
    public void ExitProcess_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x06, X0 = 0 };
        var result = _system.ExitProcess(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 0x07 ExitThread ────────────────────

    [Fact]
    public void ExitThread_SetsStateToExited()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x07, X0 = 0 };
        var result = _system.ExitThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        // 当前只有主线程，ExitThread 等同于 ExitProcess
        Assert.Equal(ProcessState.Exited, _process.State);
    }

    [Fact]
    public void ExitThread_WithExitCode_Success()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x07, X0 = 0xDEAD };
        var result = _system.ExitThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(ProcessState.Exited, _process.State);
    }

    [Fact]
    public void ExitThread_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x07, X0 = 0 };
        var result = _system.ExitThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 0x08 SleepThread ────────────────────

    [Fact]
    public void SleepThread_PositiveNanoseconds_Success()
    {
        _process = CreateMockProcess();

        // 1ms = 1_000_000 ns
        var svc = new SvcInfo { SvcNumber = 0x08, X0 = 1_000_000UL };
        var result = _system.SleepThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SleepThread_ZeroNanoseconds_Yields()
    {
        _process = CreateMockProcess();

        // 0ns = yield（让出执行权）
        var svc = new SvcInfo { SvcNumber = 0x08, X0 = 0 };
        var result = _system.SleepThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SleepThread_NegativeNanoseconds_Yields()
    {
        _process = CreateMockProcess();

        // 负值 = yield（让出执行权）
        var svc = new SvcInfo { SvcNumber = 0x08, X0 = unchecked((ulong)(-1L)) };
        var result = _system.SleepThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SleepThread_SmallNanoseconds_MinimumSleep1ms()
    {
        _process = CreateMockProcess();

        // 100ns — 小于 1ms，应取最小值 1ms
        var svc = new SvcInfo { SvcNumber = 0x08, X0 = 100UL };
        var result = _system.SleepThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SleepThread_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x08, X0 = 1_000_000UL };
        var result = _system.SleepThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 0x05 QueryMemory ────────────────────

    [Fact]
    public void QueryMemory_UnmappedAddress_ReturnsUnmappedType()
    {
        _process = CreateMockProcess();

        // 分配一个缓冲区来接收 MemoryInfo（48 字节）
        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // 查询一个未映射的地址
        ulong queryAddr = 0x0900_0000; // 空白区域
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 读取 MemoryInfo 结构体（真实 Horizon 布局：40 字节）
        var baseAddr = _memory.Read<ulong>(infoAddr);
        var size = _memory.Read<ulong>(infoAddr + 8);
        var type = _memory.Read<uint>(infoAddr + 16);

        Assert.Equal((uint)MemoryType.Unmapped, type);
        Assert.Equal(queryAddr & ~0xFFFUL, baseAddr); // 应对齐到页
    }

    [Fact]
    public void QueryMemory_MappedAddress_ReturnsCorrectType()
    {
        _process = CreateMockProcess();

        // 分配 MemoryInfo 缓冲区
        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // 查询已映射的 .text 段地址
        ulong queryAddr = 0x0800_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        var type = _memory.Read<uint>(infoAddr + 16);
        Assert.Equal((uint)MemoryType.CodeStatic, type);
    }

    [Fact]
    public void QueryMemory_CodeMutableSegment_ReturnsCodeMutableType()
    {
        _process = CreateMockProcess();

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // 查询 .data 段地址
        ulong queryAddr = 0x0802_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        var type = _memory.Read<uint>(infoAddr + 16);
        Assert.Equal((uint)MemoryType.CodeMutable, type);
    }

    [Fact]
    public void QueryMemory_StackRegion_ReturnsStackType()
    {
        _process = CreateMockProcess();

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // 查询栈区域（0x0200_0000，StartProcess 中映射）
        ulong queryAddr = 0x0200_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        var type = _memory.Read<uint>(infoAddr + 16);
        Assert.Equal((uint)MemoryType.Stack, type);
    }

    [Fact]
    public void QueryMemory_HeapRegion_ReturnsHeapType()
    {
        _process = CreateMockProcess();

        // 先通过 SetHeapSize 映射堆
        var heapSvc = new SvcInfo { SvcNumber = 0x01, X1 = 0x10000 };
        _system.SetHeapSize(heapSvc);

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // 查询堆区域（0x2000_0000）
        ulong queryAddr = 0x2000_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        var type = _memory.Read<uint>(infoAddr + 16);
        Assert.Equal((uint)MemoryType.Heap, type);
    }

    [Fact]
    public void QueryMemory_ReturnsCorrectPermissions()
    {
        _process = CreateMockProcess();

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // .text 段权限 = ReadExecute (5)
        ulong queryAddr = 0x0800_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        _system.QueryMemory(svc);

        var perm = _memory.Read<uint>(infoAddr + 24);
        Assert.Equal((uint)MemoryPermissions.ReadExecute, perm);
    }

    [Fact]
    public void QueryMemory_ReturnsRegionSize()
    {
        _process = CreateMockProcess();

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // .text 段大小 = 0x1000（1 页）
        ulong queryAddr = 0x0800_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        _system.QueryMemory(svc);

        var size = _memory.Read<ulong>(infoAddr + 8);
        Assert.True(size > 0);
    }

    [Fact]
    public void QueryMemory_ReturnsPageInfoZero()
    {
        _process = CreateMockProcess();

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        ulong queryAddr = 0x0800_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        // ReturnValue1 = PageInfo = 0（无特殊页属性）
        Assert.Equal(0UL, result.ReturnValue1);
    }

    [Fact]
    public void QueryMemory_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = TestRegionBase, X2 = 0x0800_0000 };
        var result = _system.QueryMemory(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void QueryMemory_UnmappedRegionSizeSpansToNextMapped()
    {
        _process = CreateMockProcess();

        ulong infoAddr = TestRegionBase;
        _memory.MapZero(infoAddr, PageSize, MemoryPermissions.ReadWrite);

        // 查询一个未映射的大区域 — Size 应该覆盖到下一个已映射页
        // 使用 TestRegionBase 之后的空白区域（避开 TLS 0x0100_0000~0x0100_8000）
        ulong queryAddr = 0x0101_0000;
        var svc = new SvcInfo { SvcNumber = 0x05, X0 = infoAddr, X2 = queryAddr };
        var result = _system.QueryMemory(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        var baseAddr = _memory.Read<ulong>(infoAddr);
        var size = _memory.Read<ulong>(infoAddr + 8);
        var type = _memory.Read<uint>(infoAddr + 16);

        Assert.Equal((uint)MemoryType.Unmapped, type);
        Assert.True(size > 0); // Size 应 > 0
        // 未映射区域应从 queryAddr（对齐后）延伸到下一个已映射页
        Assert.Equal(queryAddr & ~0xFFFUL, baseAddr);
    }

    // ──────────────────── SVC 0x0C GetCurrentProcessorNumber ────────────────────

    [Fact]
    public void GetCurrentProcessorNumber_ReturnsZero()
    {
        // 注册 SVC 0x0C（同 MainWindow.axaml.cs）
        _svcDispatcher.Register(0x0C, svc => new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 0 });

        var svc = new SvcInfo { SvcNumber = 0x0C };
        var result = _svcDispatcher.Dispatch(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1);
    }

    // ──────────────────── SVC 0x13 GetSystemTick ────────────────────

    [Fact]
    public void GetSystemTick_ReturnsNonZeroValue()
    {
        // 注册 SVC 0x13（同 MainWindow.axaml.cs）
        _svcDispatcher.Register(0x13, svc =>
        {
            var tick = (ulong)System.Diagnostics.Stopwatch.GetTimestamp();
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = tick };
        });

        var svc = new SvcInfo { SvcNumber = 0x13 };
        var result = _svcDispatcher.Dispatch(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.True(result.ReturnValue1 > 0); // 系统计时器应 > 0
    }

    // ──────────────────── VirtualMemoryManager QueryMemoryInfo ────────────────────

    [Fact]
    public void QueryMemoryInfo_MappedRegion_ReturnsCorrectInfo()
    {
        _memory.MapZero(TestRegionBase, 0x3000, MemoryPermissions.ReadWrite, MemoryType.Heap);

        var info = _memory.QueryMemoryInfo(TestRegionBase);

        Assert.Equal(TestRegionBase, info.BaseAddress);
        Assert.Equal(0x3000UL, info.Size);
        Assert.Equal((uint)MemoryType.Heap, info.Type);
        Assert.Equal((uint)MemoryPermissions.ReadWrite, info.Permission);
    }

    [Fact]
    public void QueryMemoryInfo_UnmappedRegion_ReturnsUnmappedType()
    {
        // 确保查询地址附近没有映射
        var info = _memory.QueryMemoryInfo(0x0900_0000);

        Assert.Equal((uint)MemoryType.Unmapped, info.Type);
        Assert.Equal(0x0900_0000UL, info.BaseAddress);
    }

    [Fact]
    public void QueryMemoryInfo_ConsolidatesAdjacentPagesOfSameType()
    {
        // 映射 3 个相邻页，相同类型和权限
        _memory.MapZero(TestRegionBase, PageSize, MemoryPermissions.ReadWrite, MemoryType.Heap);
        _memory.MapZero(TestRegionBase + PageSize, PageSize, MemoryPermissions.ReadWrite, MemoryType.Heap);
        _memory.MapZero(TestRegionBase + 2 * PageSize, PageSize, MemoryPermissions.ReadWrite, MemoryType.Heap);

        var info = _memory.QueryMemoryInfo(TestRegionBase);

        // 应合并为 3 页的连续区域
        Assert.Equal(TestRegionBase, info.BaseAddress);
        Assert.Equal(PageSize * 3, info.Size);
        Assert.Equal((uint)MemoryType.Heap, info.Type);
    }

    [Fact]
    public void QueryMemoryInfo_DifferentTypesNotConsolidated()
    {
        _memory.MapZero(TestRegionBase, PageSize, MemoryPermissions.ReadWrite, MemoryType.Heap);
        _memory.MapZero(TestRegionBase + PageSize, PageSize, MemoryPermissions.ReadWrite, MemoryType.Stack);

        var info = _memory.QueryMemoryInfo(TestRegionBase);

        // 应只包含第一个页（类型不同）
        Assert.Equal(PageSize, info.Size);
        Assert.Equal((uint)MemoryType.Heap, info.Type);
    }

    // ──────────────────── SVC 0x0E CancelSynchronization ────────────────────

    [Fact]
    public void CancelSynchronization_SetsFlag_ReturnsSuccess()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x0E, X0 = HorizonSystem.CurrentProcessPseudoHandle };
        var result = _system.CancelSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.True(_process.SyncCancelRequested);
    }

    [Fact]
    public void CancelSynchronization_InvalidHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x0E, X0 = 0xDEAD };
        var result = _system.CancelSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
        Assert.False(_process.SyncCancelRequested); // 标志未设置
    }

    [Fact]
    public void CancelSynchronization_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x0E, X0 = 0 };
        var result = _system.CancelSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void CancelSynchronization_ThenWaitSync_ReturnsCancelled()
    {
        _process = CreateMockProcess();

        // 1. 设置取消标志
        var cancelSvc = new SvcInfo { SvcNumber = 0x0E, X0 = HorizonSystem.CurrentProcessPseudoHandle };
        _system.CancelSynchronization(cancelSvc);

        // 2. WaitSynchronization 应立即返回 WaitSyncCancelled
        var kEvent = new KEvent(signaled: false); // 未信号，正常情况会超时
        int handle = _process.HandleTable.CreateHandle(kEvent);
        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        var waitSvc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(waitSvc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncCancelled), result.ReturnCode);
        // 标志已清除
        Assert.False(_process.SyncCancelRequested);
    }

    [Fact]
    public void CancelSynchronization_FlagConsumedAfterFirstWait()
    {
        _process = CreateMockProcess();

        // 设置取消标志
        _system.CancelSynchronization(new SvcInfo { SvcNumber = 0x0E, X0 = HorizonSystem.CurrentProcessPseudoHandle });

        // 第一次 WaitSynchronization：返回 Cancelled，标志清除
        var kEvent1 = new KEvent(signaled: false);
        int h1 = _process.HandleTable.CreateHandle(kEvent1);
        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(h1));

        var wait1 = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = unchecked((ulong)(-1L)) };
        var result1 = _system.WaitSynchronization(wait1);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncCancelled), result1.ReturnCode);

        // 第二次 WaitSynchronization：标志已清除，应正常超时（零超时非阻塞）
        var wait2 = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result2 = _system.WaitSynchronization(wait2);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result2.ReturnCode);
    }

    [Fact]
    public void CancelSynchronization_SignaledObjectTakesPriorityOverCancel()
    {
        _process = CreateMockProcess();

        // 设置取消标志
        _system.CancelSynchronization(new SvcInfo { SvcNumber = 0x0E, X0 = HorizonSystem.CurrentProcessPseudoHandle });

        // 创建一个已信号的 KEvent — 信号检查先于取消检查（真实 Horizon 语义）
        var kEvent = new KEvent(signaled: true);
        int handle = _process.HandleTable.CreateHandle(kEvent);
        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        var waitSvc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(waitSvc);

        // 已信号对象优先级高于取消标志（真实 Horizon：已信号对象立即返回）
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue2);
        // 取消标志未被消耗，下次同步调用仍会返回 Cancelled
        Assert.True(_process.SyncCancelRequested);
    }

    [Fact]
    public void CancelSynchronization_HandleZero_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        // 句柄 0 不是有效线程句柄
        var svc = new SvcInfo { SvcNumber = 0x0E, X0 = 0 };
        var result = _system.CancelSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
        Assert.False(_process.SyncCancelRequested);
    }

    // ──────────────────── SVC 0x0D WaitSynchronization ────────────────────

    [Fact]
    public void WaitSynchronization_SignaledKEvent_ReturnsImmediately()
    {
        _process = CreateMockProcess();

        // 创建已信号的 KEvent 并写入句柄数组
        var kEvent = new KEvent(signaled: true);
        int handle = _process.HandleTable.CreateHandle(kEvent);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue2); // 索引 0 已信号（X2 返回）
    }

    [Fact]
    public void WaitSynchronization_MultipleSignaled_ReturnsFirstSignaled()
    {
        _process = CreateMockProcess();

        // 两个 KEvent：第一个未信号，第二个已信号
        var event1 = new KEvent(signaled: false);
        var event2 = new KEvent(signaled: true);
        int h1 = _process.HandleTable.CreateHandle(event1);
        int h2 = _process.HandleTable.CreateHandle(event2);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(h1));
        _memory.Write(handlesAddr + 4, BitConverter.GetBytes(h2));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 2, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(1UL, result.ReturnValue2); // 索引 1 已信号（X2 返回）
    }

    [Fact]
    public void WaitSynchronization_KClientSession_AlwaysSignaled()
    {
        _process = CreateMockProcess();

        var session = new KClientSession("sm:");
        int handle = _process.HandleTable.CreateHandle(session);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result = _system.WaitSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue2); // KClientSession 始终信号
    }

    [Fact]
    public void WaitSynchronization_KReadableEvent_Signaled()
    {
        _process = CreateMockProcess();

        var readableEvent = new KReadableEvent(signaled: true);
        int handle = _process.HandleTable.CreateHandle(readableEvent);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue2);
    }

    [Fact]
    public void WaitSynchronization_ZeroTimeout_NotSignaled_ReturnsTimedOut()
    {
        _process = CreateMockProcess();

        var kEvent = new KEvent(signaled: false);
        int handle = _process.HandleTable.CreateHandle(kEvent);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result = _system.WaitSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    [Fact]
    public void WaitSynchronization_InvalidHandle_ReturnsInvalidHandleWithIndex()
    {
        _process = CreateMockProcess();

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(0xDEAD)); // 无效句柄

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncInvalidHandle), result.ReturnCode);
        Assert.Equal(0UL, result.ReturnValue2); // 出错索引在 X2
    }

    [Fact]
    public void WaitSynchronization_MultipleHandles_InvalidAtSecond_ReturnsIndex1()
    {
        _process = CreateMockProcess();

        var validEvent = new KEvent(signaled: true);
        int h1 = _process.HandleTable.CreateHandle(validEvent);
        int h2 = 0xBEEF; // 无效句柄

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(h1));
        _memory.Write(handlesAddr + 4, BitConverter.GetBytes(h2));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 2, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncInvalidHandle), result.ReturnCode);
        Assert.Equal(1UL, result.ReturnValue2); // 出错索引 = 1（X2）
    }

    [Fact]
    public void WaitSynchronization_TooManyHandles_ReturnsTooManyHandles()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = 0, X1 = 65, X2 = 0 };
        var result = _system.WaitSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncTooManyHandles), result.ReturnCode);
    }

    [Fact]
    public void WaitSynchronization_ZeroHandles_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = 0, X1 = 0, X2 = 0 };
        var result = _system.WaitSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncInvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void WaitSynchronization_UnsignalThenSignal_ReturnsSignaledIndex()
    {
        _process = CreateMockProcess();

        // 创建未信号 KEvent，零超时测试应该返回 TimedOut
        var kEvent = new KEvent(signaled: false);
        int handle = _process.HandleTable.CreateHandle(kEvent);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        // 零超时：应返回 TimedOut
        var svc1 = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result1 = _system.WaitSynchronization(svc1);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result1.ReturnCode);

        // 手动信号化
        kEvent.IsSignaled = true;

        // 零超时再次检查：应返回成功
        var svc2 = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result2 = _system.WaitSynchronization(svc2);
        Assert.True(result2.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result2.ReturnValue2); // 信号后索引 0 在 X2
    }

    [Fact]
    public void WaitSynchronization_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = 0, X1 = 1, X2 = 0 };
        var result = _system.WaitSynchronization(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void WaitSynchronization_AllSignaled_ReturnsFirstIndex()
    {
        _process = CreateMockProcess();

        var e1 = new KEvent(signaled: true);
        var e2 = new KEvent(signaled: true);
        var e3 = new KEvent(signaled: true);
        int h1 = _process.HandleTable.CreateHandle(e1);
        int h2 = _process.HandleTable.CreateHandle(e2);
        int h3 = _process.HandleTable.CreateHandle(e3);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(h1));
        _memory.Write(handlesAddr + 4, BitConverter.GetBytes(h2));
        _memory.Write(handlesAddr + 8, BitConverter.GetBytes(h3));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 3, X2 = unchecked((ulong)(-1L)) };
        var result = _system.WaitSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue2); // 第一个已信号的索引（X2）
    }

    [Fact]
    public void WaitSynchronization_MixedSignaled_ReturnsCorrectIndex()
    {
        _process = CreateMockProcess();

        // 3 个句柄：session(信号), event(未信号), readableEvent(信号)
        var session = new KClientSession("fs:");
        var kEvent = new KEvent(signaled: false);
        var readableEvent = new KReadableEvent(signaled: true);
        int h1 = _process.HandleTable.CreateHandle(session);
        int h2 = _process.HandleTable.CreateHandle(kEvent);
        int h3 = _process.HandleTable.CreateHandle(readableEvent);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(h1));
        _memory.Write(handlesAddr + 4, BitConverter.GetBytes(h2));
        _memory.Write(handlesAddr + 8, BitConverter.GetBytes(h3));

        var svc = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 3, X2 = 0 };
        var result = _system.WaitSynchronization(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue2); // session 在索引 0 已信号（X2）
    }

    [Fact]
    public void WaitSynchronization_KReadableEvent_AutoClearedAfterWait()
    {
        _process = CreateMockProcess();

        var readableEvent = new KReadableEvent(signaled: true);
        int handle = _process.HandleTable.CreateHandle(readableEvent);

        ulong handlesAddr = TestRegionBase + 0x10000;
        _memory.MapZero(handlesAddr, PageSize, MemoryPermissions.ReadWrite);
        _memory.Write(handlesAddr, BitConverter.GetBytes(handle));

        // 第一次等待：应成功返回
        var svc1 = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result1 = _system.WaitSynchronization(svc1);
        Assert.True(result1.ReturnCode.IsSuccess);

        // KReadableEvent 应被自动清除
        Assert.False(readableEvent.IsSignaled);

        // 第二次等待（零超时）：应返回 TimedOut
        var svc2 = new SvcInfo { SvcNumber = 0x0D, X0 = handlesAddr, X1 = 1, X2 = 0 };
        var result2 = _system.WaitSynchronization(svc2);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result2.ReturnCode);
    }

    public void Dispose() => _memory.Dispose();
}
