using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

/// <summary>
/// SVC 0x34 CreateThread 单元测试
/// 验证线程创建、参数校验、TLS 分配、句柄注册等
/// </summary>
public class CreateThreadTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    // 测试用地址
    private const ulong EntryPoint = 0x0800_0000; // NRO 代码段内

    public CreateThreadTests()
    {
        _system = new HorizonSystem(_memory, _svcDispatcher);
    }

    private HorizonProcess CreateMockProcess()
    {
        var nroModule = new NroModule
        {
            EntryPoint = EntryPoint,
            Header = new NroHeader
            {
                Magic = 0, Version = 0,
                TextSize = 0x1000, RodataSize = 0x1000,
                DataSize = 0x1000, BssSize = 0x1000,
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

        var processInfo = new ProcessInfo { Name = "CreateThreadTest", EntryPoint = nroModule.EntryPoint };
        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    private static SvcInfo MakeCreateThreadSvc(ulong entryPoint = EntryPoint, ulong argument = 0,
        ulong stackTop = 0, int priority = 44, int processorId = -2)
    {
        return new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = entryPoint,
            X1 = argument,
            X2 = stackTop,
            X3 = (ulong)unchecked((long)priority),
            X4 = (ulong)unchecked((long)processorId),
        };
    }

    // ──────────────────── 成功路径测试 ────────────────────

    [Fact]
    public void CreateThread_ValidParams_ReturnsSuccessWithHandle()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.NotEqual(0UL, result.ReturnValue1); // 句柄非零
    }

    [Fact]
    public void CreateThread_ValidParams_HandleIsInHandleTable()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        int handle = (int)result.ReturnValue1;
        Assert.True(_process.HandleTable.IsValid(handle));

        var obj = _process.HandleTable.GetObject(handle);
        Assert.NotNull(obj);
        Assert.IsType<KThread>(obj);
    }

    [Fact]
    public void CreateThread_KThreadHasCorrectMetadata()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(entryPoint: 0x0800_1234, argument: 0xABCD,
            stackTop: 0, priority: 30, processorId: 1);
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        int handle = (int)result.ReturnValue1;
        var thread = _process.HandleTable.GetObject<KThread>(handle);

        Assert.NotNull(thread);
        Assert.Equal(0x0800_1234UL, thread!.EntryPoint);
        Assert.Equal(0xABCDUL, thread.Argument);
        Assert.Equal(30, thread.Priority);
        Assert.Equal(1, thread.ProcessorId);
        Assert.Equal(Horizon.ThreadState.Created, thread.State); // 新线程处于 Created 状态
        Assert.NotEqual(0UL, thread.TlsAddress); // 已分配 TLS
        Assert.NotEqual(0UL, thread.StackTop); // 已分配栈
        Assert.NotEqual(0UL, thread.ThreadId); // 有线程 ID
    }

    [Fact]
    public void CreateThread_StackTopZero_KernelAllocatesStack()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(stackTop: 0);
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        int handle = (int)result.ReturnValue1;
        var thread = _process.HandleTable.GetObject<KThread>(handle);

        Assert.NotNull(thread);
        // 内核分配栈：StackTop 非零，StackSize = DefaultThreadStackSize (0x40000)
        Assert.NotEqual(0UL, thread!.StackTop);
        Assert.Equal(0x40000UL, thread.StackSize);
        Assert.NotEqual(0UL, thread.StackBase);
    }

    [Fact]
    public void CreateThread_StackTopProvided_UsesProvidedStack()
    {
        _process = CreateMockProcess();

        // 映射一个栈区域供 NRO 传入
        ulong userStackBase = 0x0400_0000;
        ulong userStackSize = 0x80000;
        _memory.MapZero(userStackBase, userStackSize, MemoryPermissions.ReadWrite, MemoryType.Stack);
        ulong userStackTop = userStackBase + userStackSize;

        var svc = MakeCreateThreadSvc(stackTop: userStackTop);
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        int handle = (int)result.ReturnValue1;
        var thread = _process.HandleTable.GetObject<KThread>(handle);

        Assert.NotNull(thread);
        Assert.Equal(userStackTop, thread!.StackTop);
    }

    [Fact]
    public void CreateThread_TlsSlotAllocated_NotOverlappingWithMainThread()
    {
        _process = CreateMockProcess();

        ulong mainTls = _process.TlsAddress; // 主线程 TLS

        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        int handle = (int)result.ReturnValue1;
        var thread = _process.HandleTable.GetObject<KThread>(handle);

        Assert.NotNull(thread);
        Assert.NotEqual(mainTls, thread!.TlsAddress); // 不同于主线程 TLS
        Assert.Equal(mainTls + 0x200, thread.TlsAddress); // slot 1 = base + 0x200
    }

    [Fact]
    public void CreateThread_MultipleThreads_GetDifferentTlsSlots()
    {
        _process = CreateMockProcess();

        var result1 = _system.CreateThread(MakeCreateThreadSvc());
        var result2 = _system.CreateThread(MakeCreateThreadSvc());
        var result3 = _system.CreateThread(MakeCreateThreadSvc());

        Assert.True(result1.ReturnCode.IsSuccess);
        Assert.True(result2.ReturnCode.IsSuccess);
        Assert.True(result3.ReturnCode.IsSuccess);

        var t1 = _process.HandleTable.GetObject<KThread>((int)result1.ReturnValue1);
        var t2 = _process.HandleTable.GetObject<KThread>((int)result2.ReturnValue1);
        var t3 = _process.HandleTable.GetObject<KThread>((int)result3.ReturnValue1);

        Assert.NotNull(t1);
        Assert.NotNull(t2);
        Assert.NotNull(t3);

        // TLS 地址应各不相同，间距 0x200
        Assert.Equal(t1!.TlsAddress + 0x200, t2!.TlsAddress);
        Assert.Equal(t2.TlsAddress + 0x200, t3!.TlsAddress);
    }

    [Fact]
    public void CreateThread_MultipleThreads_GetDifferentHandles()
    {
        _process = CreateMockProcess();

        var result1 = _system.CreateThread(MakeCreateThreadSvc());
        var result2 = _system.CreateThread(MakeCreateThreadSvc());

        Assert.NotEqual(result1.ReturnValue1, result2.ReturnValue1);
    }

    [Fact]
    public void CreateThread_MultipleThreads_GetDifferentThreadIds()
    {
        _process = CreateMockProcess();

        var result1 = _system.CreateThread(MakeCreateThreadSvc());
        var result2 = _system.CreateThread(MakeCreateThreadSvc());

        var t1 = _process.HandleTable.GetObject<KThread>((int)result1.ReturnValue1);
        var t2 = _process.HandleTable.GetObject<KThread>((int)result2.ReturnValue1);

        Assert.NotNull(t1);
        Assert.NotNull(t2);
        Assert.NotEqual(t1!.ThreadId, t2!.ThreadId);
    }

    [Fact]
    public void CreateThread_ThreadIsWaitable_ButNotSignaledUntilTerminated()
    {
        _process = CreateMockProcess();

        var result = _system.CreateThread(MakeCreateThreadSvc());
        int handle = (int)result.ReturnValue1;
        var thread = _process.HandleTable.GetObject<KThread>(handle);

        Assert.NotNull(thread);
        Assert.False(thread!.IsSignaled); // Created 状态未信号

        // 模拟线程终止
        thread.State = Horizon.ThreadState.Terminated;
        Assert.True(thread.IsSignaled); // 终止后信号
    }

    // ──────────────────── 优先级验证测试 ────────────────────

    [Fact]
    public void CreateThread_PriorityOutOfRange_Negative_ReturnsInvalidPriority()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(priority: -1);
        var result = _system.CreateThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidPriority), result.ReturnCode);
    }

    [Fact]
    public void CreateThread_PriorityOutOfRange_Above63_ReturnsInvalidPriority()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(priority: 64);
        var result = _system.CreateThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidPriority), result.ReturnCode);
    }

    [Theory]
    [InlineData(0)]   // 最低优先级
    [InlineData(44)]  // 标准应用线程
    [InlineData(63)]  // 最高优先级
    public void CreateThread_ValidPriority_Succeeds(int priority)
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(priority: priority);
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    // ──────────────────── 处理器 ID 验证测试 ────────────────────

    [Theory]
    [InlineData(0)]   // 核心 0
    [InlineData(1)]   // 核心 1
    [InlineData(2)]   // 核心 2
    [InlineData(3)]   // 核心 3
    [InlineData(-1)]  // 默认核心
    [InlineData(-2)]  // 任意核心 (0xFFFFFFFE)
    public void CreateThread_ValidProcessorId_Succeeds(int processorId)
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(processorId: processorId);
        var result = _system.CreateThread(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Theory]
    [InlineData(-3)]   // 无效
    [InlineData(4)]    // 超出核心数
    [InlineData(99)]   // 远超范围
    public void CreateThread_InvalidProcessorId_ReturnsInvalidProcessorId(int processorId)
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(processorId: processorId);
        var result = _system.CreateThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidProcessorId), result.ReturnCode);
    }

    // ──────────────────── 入口点验证测试 ────────────────────

    [Fact]
    public void CreateThread_EntryPointZero_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(entryPoint: 0);
        var result = _system.CreateThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void CreateThread_EntryPointOutsideAddressSpace_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc(entryPoint: 0xFFFF_0000_0000_0000);
        var result = _system.CreateThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    // ──────────────────── 进程状态验证测试 ────────────────────

    [Fact]
    public void CreateThread_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── 线程数量限制测试 ────────────────────

    [Fact]
    public void CreateThread_ExceedsThreadLimit_ReturnsOutOfResource()
    {
        _process = CreateMockProcess();

        // 创建 63 个线程（主线程已占 1，总共 64）
        for (int i = 0; i < 63; i++)
        {
            var result = _system.CreateThread(MakeCreateThreadSvc());
            Assert.True(result.ReturnCode.IsSuccess, $"第 {i + 1} 个线程创建应成功");
        }

        // 第 64 个线程应失败
        var overLimit = _system.CreateThread(MakeCreateThreadSvc());
        Assert.Equal(ResultCode.KernelResult(TKernelResult.OutOfResource), overLimit.ReturnCode);
    }

    // ──────────────────── 句柄操作集成测试 ────────────────────

    [Fact]
    public void CreateThread_HandleCanBeClosed()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);
        Assert.True(result.ReturnCode.IsSuccess);

        int handle = (int)result.ReturnValue1;

        // 句柄可通过 CloseHandle 关闭
        var closeResult = _system.CloseHandle(new SvcInfo { SvcNumber = 0x19, X0 = (ulong)handle });
        Assert.True(closeResult.ReturnCode.IsSuccess);

        // 关闭后句柄无效
        Assert.False(_process.HandleTable.IsValid(handle));
    }

    [Fact]
    public void CreateThread_HandleCanBeWaitedOn()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);
        Assert.True(result.ReturnCode.IsSuccess);

        int handle = (int)result.ReturnValue1;

        // 将句柄写入 guest 内存以便 WaitSynchronization 读取
        ulong handleArrayAddr = 0x0100_1000;
        _memory.Write(handleArrayAddr, BitConverter.GetBytes(handle));

        // 线程处于 Created 状态（未信号），零超时应返回 TimedOut
        var waitResult = _system.WaitSynchronization(new SvcInfo
        {
            SvcNumber = 0x0D,
            X0 = handleArrayAddr,
            X1 = 1,
            X2 = 0 // 零超时
        });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), waitResult.ReturnCode);
    }

    [Fact]
    public void CreateThread_TerminatedThreadCanBeWaitedOn()
    {
        _process = CreateMockProcess();

        var svc = MakeCreateThreadSvc();
        var result = _system.CreateThread(svc);
        Assert.True(result.ReturnCode.IsSuccess);

        int handle = (int)result.ReturnValue1;
        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.NotNull(thread);

        // 模拟线程终止
        thread!.State = Horizon.ThreadState.Terminated;

        // 写入句柄数组
        ulong handleArrayAddr = 0x0100_1000;
        _memory.Write(handleArrayAddr, BitConverter.GetBytes(handle));

        // 终止的线程应立即信号
        var waitResult = _system.WaitSynchronization(new SvcInfo
        {
            SvcNumber = 0x0D,
            X0 = handleArrayAddr,
            X1 = 1,
            X2 = 0
        });
        Assert.True(waitResult.ReturnCode.IsSuccess);
    }

    // ──────────────────── SVC 分发集成测试 ────────────────────

    [Fact]
    public void CreateThread_ViaSvcDispatcher_ReturnsSuccess()
    {
        _process = CreateMockProcess();

        _svcDispatcher.Register(0x34, svc => _system.CreateThread(svc));

        var svc = MakeCreateThreadSvc();
        var result = _svcDispatcher.Dispatch(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.NotEqual(0UL, result.ReturnValue1);
    }

    [Fact]
    public void CreateThread_SvcName_Registered()
    {
        Assert.Equal("CreateThread", _svcDispatcher.GetSvcName(0x34));
    }

    // ──────────────────── 释放线程槽测试 ────────────────────

    [Fact]
    public void CreateThread_ReleaseThreadSlot_AllowsNewThread()
    {
        _process = CreateMockProcess();

        // 创建 63 个线程达到上限
        for (int i = 0; i < 63; i++)
        {
            _system.CreateThread(MakeCreateThreadSvc());
        }

        // 下一个应失败
        var overLimit = _system.CreateThread(MakeCreateThreadSvc());
        Assert.Equal(ResultCode.KernelResult(TKernelResult.OutOfResource), overLimit.ReturnCode);

        // 释放一个线程槽
        _process.ReleaseThreadSlot();

        // 现在应成功
        var afterRelease = _system.CreateThread(MakeCreateThreadSvc());
        Assert.True(afterRelease.ReturnCode.IsSuccess);
    }

    public void Dispose() => _memory.Dispose();
}
