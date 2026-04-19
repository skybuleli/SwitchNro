using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

/// <summary>
/// SVC 0x40 SetThreadActivity 单元测试
/// 验证线程暂停/恢复、状态转换、无效参数、句柄校验等
/// </summary>
public class SetThreadActivityTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    private const ulong EntryPoint = 0x0800_0000;

    public SetThreadActivityTests()
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

        var processInfo = new ProcessInfo { Name = "SetThreadActivityTest", EntryPoint = nroModule.EntryPoint };
        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    /// <summary>创建并启动一个线程，返回其句柄</summary>
    private int CreateAndStartThread(ulong entryPoint = EntryPoint, int priority = 44)
    {
        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = entryPoint,
            X1 = 0,
            X2 = 0,
            X3 = (ulong)priority,
            X4 = unchecked((ulong)(long)-2),
        });
        Assert.True(createResult.ReturnCode.IsSuccess);
        int handle = (int)createResult.ReturnValue1;

        var startResult = _system.StartThread(new SvcInfo { SvcNumber = 0x4C, X0 = (ulong)handle });
        Assert.True(startResult.ReturnCode.IsSuccess);
        return handle;
    }

    /// <summary>创建但启动一个线程（Created 状态），返回其句柄</summary>
    private int CreateOnlyThread()
    {
        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = EntryPoint,
            X1 = 0,
            X2 = 0,
            X3 = 44,
            X4 = unchecked((ulong)(long)-2),
        });
        Assert.True(createResult.ReturnCode.IsSuccess);
        return (int)createResult.ReturnValue1;
    }

    // ──────────────────── 暂停/恢复成功路径测试 ────────────────────

    [Fact]
    public void SetThreadActivity_Pause_RunningThread_TransitionsToPaused()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        var result = _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)handle,
            X1 = (ulong)ThreadActivity.Paused,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.Equal(Horizon.ThreadState.Paused, thread!.State);
    }

    [Fact]
    public void SetThreadActivity_Resume_PausedThread_TransitionsToRunning()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        // 先暂停
        _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)handle,
            X1 = (ulong)ThreadActivity.Paused,
        });

        // 再恢复
        var result = _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)handle,
            X1 = (ulong)ThreadActivity.Runnable,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.Equal(Horizon.ThreadState.Running, thread!.State);
    }

    [Fact]
    public void SetThreadActivity_PauseResumeRoundTrip_ReturnsToRunning()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();
        var thread = _process.HandleTable.GetObject<KThread>(handle);

        // Running → Paused
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        Assert.Equal(Horizon.ThreadState.Paused, thread!.State);

        // Paused → Running
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 0 });
        Assert.Equal(Horizon.ThreadState.Running, thread.State);

        // 可以再次暂停
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        Assert.Equal(Horizon.ThreadState.Paused, thread.State);
    }

    [Fact]
    public void SetThreadActivity_PauseMainThread_Succeeds()
    {
        _process = CreateMockProcess();

        // 主线程是 Running 状态，可以暂停
        var result = _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = (ulong)ThreadActivity.Paused,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        var mainThread = _process.HandleTable.GetObject<KThread>(_process.MainThreadHandle);
        Assert.Equal(Horizon.ThreadState.Paused, mainThread!.State);
    }

    // ──────────────────── 伪句柄测试 ────────────────────

    [Fact]
    public void SetThreadActivity_PseudoHandle_PauseSucceeds()
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = HorizonSystem.CurrentProcessPseudoHandle,
            X1 = (ulong)ThreadActivity.Paused,
        });

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SetThreadActivity_PseudoHandle_ResumeAlreadyRunning_ReturnsInvalidState()
    {
        _process = CreateMockProcess();

        // 伪句柄指向主线程，主线程是 Running，恢复应返回 InvalidState
        var result = _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = HorizonSystem.CurrentProcessPseudoHandle,
            X1 = (ulong)ThreadActivity.Runnable,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── 重复操作测试 ────────────────────

    [Fact]
    public void SetThreadActivity_PauseAlreadyPaused_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        // 第一次暂停成功
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });

        // 重复暂停返回 InvalidState
        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_ResumeAlreadyRunning_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        // 线程已是 Running，恢复返回 InvalidState
        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 0 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── Created/Terminated 状态测试 ────────────────────

    [Fact]
    public void SetThreadActivity_PauseCreatedThread_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateOnlyThread(); // Created 状态

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_ResumeCreatedThread_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateOnlyThread(); // Created 状态

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 0 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_PauseTerminatedThread_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        // 模拟终止
        var thread = _process.HandleTable.GetObject<KThread>(handle);
        thread!.State = Horizon.ThreadState.Terminated;

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_ResumeTerminatedThread_ReturnsInvalidState()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        thread!.State = Horizon.ThreadState.Terminated;

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 0 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── 无效 Activity 值测试 ────────────────────

    [Fact]
    public void SetThreadActivity_InvalidActivityValue2_ReturnsInvalidThreadActivity()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 2 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidThreadActivity), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_InvalidActivityValueNegative_ReturnsInvalidThreadActivity()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        var result = _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)handle,
            X1 = unchecked((ulong)(long)-1),
        });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidThreadActivity), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_LargeActivityValue_ReturnsInvalidThreadActivity()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 100 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidThreadActivity), result.ReturnCode);
    }

    // ──────────────────── 句柄验证测试 ────────────────────

    [Fact]
    public void SetThreadActivity_HandleZero_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = 0, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_InvalidHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = 0xDEADBEEF, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_NonThreadHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var session = new KClientSession("test:");
        int sessionHandle = _process.HandleTable.CreateHandle(session);

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)sessionHandle, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void SetThreadActivity_ClosedHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        _system.CloseHandle(new SvcInfo { SvcNumber = 0x19, X0 = (ulong)handle });

        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    // ──────────────────── 进程状态验证 ────────────────────

    [Fact]
    public void SetThreadActivity_NoActiveProcess_ReturnsInvalidState()
    {
        var result = _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = 0xD000, X1 = 1 });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── IsSignaled 语义测试 ────────────────────

    [Fact]
    public void SetThreadActivity_PausedThread_NotSignaled()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.False(thread!.IsSignaled); // Paused 状态不信号
    }

    [Fact]
    public void SetThreadActivity_ResumedThread_StillNotSignaled()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 0 });

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.False(thread!.IsSignaled); // Running 状态不信号（非主线程）
    }

    [Fact]
    public void SetThreadActivity_PausedThread_TerminateBecomesSignaled()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });

        var thread = _process.HandleTable.GetObject<KThread>(handle);
        Assert.False(thread!.IsSignaled);

        thread!.State = Horizon.ThreadState.Terminated;
        Assert.True(thread.IsSignaled);
    }

    // ──────────────────── 多线程暂停测试 ────────────────────

    [Fact]
    public void SetThreadActivity_MultipleThreads_IndependentPauseResume()
    {
        _process = CreateMockProcess();
        int h1 = CreateAndStartThread();
        int h2 = CreateAndStartThread();

        // 暂停 h1，h2 不受影响
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)h1, X1 = 1 });

        var t1 = _process.HandleTable.GetObject<KThread>(h1);
        var t2 = _process.HandleTable.GetObject<KThread>(h2);
        Assert.Equal(Horizon.ThreadState.Paused, t1!.State);
        Assert.Equal(Horizon.ThreadState.Running, t2!.State);

        // 恢复 h1，暂停 h2
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)h1, X1 = 0 });
        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)h2, X1 = 1 });

        Assert.Equal(Horizon.ThreadState.Running, t1.State);
        Assert.Equal(Horizon.ThreadState.Paused, t2!.State);
    }

    // ──────────────────── SVC 分发集成测试 ────────────────────

    [Fact]
    public void SetThreadActivity_ViaSvcDispatcher_ReturnsSuccess()
    {
        _process = CreateMockProcess();
        _svcDispatcher.Register(0x40, svc => _system.SetThreadActivity(svc));

        int handle = CreateAndStartThread();

        var result = _svcDispatcher.Dispatch(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)handle,
            X1 = (ulong)ThreadActivity.Paused,
        });

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SetThreadActivity_SvcName_Registered()
    {
        Assert.Equal("SetThreadActivity", _svcDispatcher.GetSvcName(0x40));
    }

    // ──────────────────── WaitSynchronization 集成测试 ────────────────────

    [Fact]
    public void SetThreadActivity_PausedThread_WaitSync_TimesOut()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread();

        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });

        // Paused 状态的线程不信号，零超时应 TimedOut
        ulong handleArrayAddr = 0x0100_1000;
        _memory.Write(handleArrayAddr, BitConverter.GetBytes(handle));

        var waitResult = _system.WaitSynchronization(new SvcInfo
        {
            SvcNumber = 0x0D,
            X0 = handleArrayAddr,
            X1 = 1,
            X2 = 0,
        });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), waitResult.ReturnCode);
    }

    // ──────────────────── 主线程暂停后 IsSignaled 行为（单线程模型已知行为） ────────────────────

    [Fact]
    public void SetThreadActivity_PausedMainThread_StillSignaledDueToMainThreadFlag()
    {
        _process = CreateMockProcess();

        // 主线程暂停后 IsSignaled 仍为 true — 这是单线程模型的已知行为
        // KThread.IsSignaled 逻辑: State == Terminated || _isMainThread
        // 主线程始终视为已信号，简化 WaitSynchronization
        _system.SetThreadActivity(new SvcInfo
        {
            SvcNumber = 0x40,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = (ulong)ThreadActivity.Paused,
        });

        var mainThread = _process.HandleTable.GetObject<KThread>(_process.MainThreadHandle);
        Assert.Equal(Horizon.ThreadState.Paused, mainThread!.State);
        Assert.True(mainThread.IsSignaled); // 主线程始终信号（_isMainThread 标志）
    }

    // ──────────────────── 优先级在暂停后仍可修改 ────────────────────

    [Fact]
    public void SetThreadActivity_PriorityStillChangeableWhenPaused()
    {
        _process = CreateMockProcess();
        int handle = CreateAndStartThread(priority: 44);

        _system.SetThreadActivity(new SvcInfo { SvcNumber = 0x40, X0 = (ulong)handle, X1 = 1 });

        // 暂停后修改优先级
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
