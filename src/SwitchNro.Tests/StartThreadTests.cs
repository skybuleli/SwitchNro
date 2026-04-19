using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

/// <summary>
/// SVC 0x4C StartThread 单元测试
/// 验证线程启动、状态转换、重复启动、已终止线程启动等
/// </summary>
public class StartThreadTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    private const ulong EntryPoint = 0x0800_0000;

    public StartThreadTests()
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

        var processInfo = new ProcessInfo { Name = "StartThreadTest", EntryPoint = nroModule.EntryPoint };
        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    /// <summary>创建一个线程并返回其句柄</summary>
    private int CreateTestThread(ulong entryPoint = EntryPoint, ulong argument = 0, int priority = 44)
    {
        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = entryPoint,
            X1 = argument,
            X2 = 0,
            X3 = (ulong)priority,
            X4 = unchecked((ulong)(long)-2),
        });
        Assert.True(createResult.ReturnCode.IsSuccess);
        return (int)createResult.ReturnValue1;
    }

    // ──────────────────── 成功路径测试 ────────────────────

    [Fact]
    public void StartThread_CreatedThread_TransitionsToRunning()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.NotNull(thread);
        Assert.Equal(Horizon.ThreadState.Created, thread!.State); // 创建后为 Created

        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(Horizon.ThreadState.Running, thread.State); // 启动后为 Running
    }

    [Fact]
    public void StartThread_ReturnsSuccess()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void StartThread_PreservesThreadMetadata()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread(entryPoint: 0x0800_1234, argument: 0xABCD, priority: 30);

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.NotNull(thread);

        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        // 元数据不变
        Assert.Equal(0x0800_1234UL, thread!.EntryPoint);
        Assert.Equal(0xABCDUL, thread.Argument);
        Assert.Equal(30, thread.Priority);
        Assert.NotEqual(0UL, thread.TlsAddress);
        Assert.NotEqual(0UL, thread.StackTop);
    }

    // ──────────────────── 重复启动测试 ────────────────────

    [Fact]
    public void StartThread_AlreadyRunning_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        // 第一次启动成功
        var result1 = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });
        Assert.True(result1.ReturnCode.IsSuccess);

        // 第二次启动应失败
        var result2 = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result2.ReturnCode);
    }

    [Fact]
    public void StartThread_TerminatedThread_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        // 启动线程
        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        // 模拟线程终止
        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.NotNull(thread);
        thread!.State = Horizon.ThreadState.Terminated;

        // 尝试启动已终止线程
        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── 句柄验证测试 ────────────────────

    [Fact]
    public void StartThread_InvalidHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = 0xDEADBEEF });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void StartThread_HandleZero_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = 0 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void StartThread_NonThreadHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        // 创建一个非 KThread 内核对象
        var session = new KClientSession("test:");
        int sessionHandle = _process.HandleTable.CreateHandle(session);

        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)sessionHandle });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void StartThread_ClosedHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        // 关闭句柄
        _system.CloseHandle(new SvcInfo { SvcNumber = 0x19, X0 = (ulong)handle });

        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    // ──────────────────── 进程状态验证 ────────────────────

    [Fact]
    public void StartThread_NoActiveProcess_ReturnsInvalidState()
    {
        var result = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = 0xD000 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── 主线程/伪句柄不可 StartThread ────────────────────

    [Fact]
    public void StartThread_MainThreadHandle_ReturnsInvalidState()
    {
        _process = CreateMockProcess();

        // 主线程已经是 Running 状态，不可再启动
        var result = _system.StartThread(new SvcInfo
        {
            SvcNumber = 0x4C,
            X0 = (ulong)_process.MainThreadHandle,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void StartThread_CurrentProcessPseudoHandle_ReturnsInvalidState()
    {
        _process = CreateMockProcess();

        // 伪句柄 0xFFFF8000 在单线程模型中等价于主线程，已是 Running 状态
        var result = _system.StartThread(new SvcInfo
        {
            SvcNumber = 0x4C,
            X0 = HorizonSystem.CurrentProcessPseudoHandle,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── IsSignaled 语义测试 ────────────────────

    [Fact]
    public void StartThread_RunningThread_NotSignaled()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.NotNull(thread);
        Assert.Equal(Horizon.ThreadState.Running, thread!.State);
        Assert.False(thread.IsSignaled); // Running 状态不信号
    }

    [Fact]
    public void StartThread_ThenTerminate_BecomesSignaled()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.NotNull(thread);
        Assert.False(thread!.IsSignaled);

        // 模拟终止
        thread.State = Horizon.ThreadState.Terminated;
        Assert.True(thread.IsSignaled); // 终止后信号
    }

    // ──────────────────── 多线程启动测试 ────────────────────

    [Fact]
    public void StartThread_MultipleThreads_AllTransitionsToRunning()
    {
        _process = CreateMockProcess();

        int h1 = CreateTestThread();
        int h2 = CreateTestThread();
        int h3 = CreateTestThread();

        var r1 = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)h1 });
        var r2 = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)h2 });
        var r3 = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)h3 });

        Assert.True(r1.ReturnCode.IsSuccess);
        Assert.True(r2.ReturnCode.IsSuccess);
        Assert.True(r3.ReturnCode.IsSuccess);

        Assert.Equal(Horizon.ThreadState.Running, _process.HandleTable.GetObject<KThread>(h1)!.State);
        Assert.Equal(Horizon.ThreadState.Running, _process.HandleTable.GetObject<KThread>(h2)!.State);
        Assert.Equal(Horizon.ThreadState.Running, _process.HandleTable.GetObject<KThread>(h3)!.State);
    }

    // ──────────────────── SVC 分发集成测试 ────────────────────

    [Fact]
    public void StartThread_ViaSvcDispatcher_ReturnsSuccess()
    {
        _process = CreateMockProcess();
        _svcDispatcher.Register(0x4C, svc => _system.StartThread(svc));

        int handle = CreateTestThread();

        var result = _svcDispatcher.Dispatch(new SvcInfo
        {
            SvcNumber = 0x4C,
            X0 = (ulong)handle,
        });

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void StartThread_SvcName_Registered()
    {
        Assert.Equal("StartThread", _svcDispatcher.GetSvcName(0x4C));
    }

    // ──────────────────── Create→Start→Wait 集成测试 ────────────────────

    [Fact]
    public void StartThread_WaitSynchronizationOnRunningThread_TimesOut()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        // Running 状态的线程不信号，零超时应 TimedOut
        ulong handleArrayAddr = 0x0100_1000;
        _memory.Write(handleArrayAddr, BitConverter.GetBytes(handle));

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
    public void StartThread_ThenTerminate_WaitSynchronizationSucceeds()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread();

        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        // 模拟终止
        var thread = _process.HandleTable.GetObject<KThread>(handle);
        thread!.State = Horizon.ThreadState.Terminated;

        ulong handleArrayAddr = 0x0100_1000;
        _memory.Write(handleArrayAddr, BitConverter.GetBytes(handle));

        var waitResult = _system.WaitSynchronization(new SvcInfo
        {
            SvcNumber = 0x0D,
            X0 = handleArrayAddr,
            X1 = 1,
            X2 = 0
        });
        Assert.True(waitResult.ReturnCode.IsSuccess);
    }

    // ──────────────────── 优先级在启动后仍可修改 ────────────────────

    [Fact]
    public void StartThread_PriorityStillChangeableAfterStart()
    {
        _process = CreateMockProcess();
        int handle = CreateTestThread(priority: 44);

        _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });

        // 启动后修改优先级
        var setPriResult = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)handle,
            X1 = 20,
        });
        Assert.True(setPriResult.ReturnCode.IsSuccess);

        var getPriResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)handle,
        });
        Assert.Equal(20UL, getPriResult.ReturnValue1);
    }

    public void Dispose() => _memory.Dispose();
}
