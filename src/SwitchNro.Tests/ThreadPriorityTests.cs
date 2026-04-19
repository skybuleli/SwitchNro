using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

/// <summary>
/// SVC 0x09 GetThreadPriority / SVC 0x0A SetThreadPriority 单元测试
/// 验证优先级读写、句柄校验、伪句柄支持、范围校验等
/// </summary>
public class ThreadPriorityTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    private const ulong EntryPoint = 0x0800_0000;

    public ThreadPriorityTests()
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

        var processInfo = new ProcessInfo { Name = "ThreadPriorityTest", EntryPoint = nroModule.EntryPoint };
        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    // ──────────────────── GetThreadPriority 测试 ────────────────────

    [Fact]
    public void GetThreadPriority_MainThreadHandle_ReturnsDefaultPriority()
    {
        _process = CreateMockProcess();

        // 主线程默认优先级 = 44
        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)_process.MainThreadHandle,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(44UL, result.ReturnValue1);
    }

    [Fact]
    public void GetThreadPriority_PseudoHandle_ReturnsMainThreadPriority()
    {
        _process = CreateMockProcess();

        // 0xFFFF8000 伪句柄应映射到主线程
        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = HorizonSystem.CurrentProcessPseudoHandle,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(44UL, result.ReturnValue1);
    }

    [Fact]
    public void GetThreadPriority_CreatedThread_ReturnsSetPriority()
    {
        _process = CreateMockProcess();

        // 创建优先级为 30 的线程
        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = EntryPoint,
            X1 = 0,
            X2 = 0,
            X3 = 30,
            X4 = unchecked((ulong)(long)-2),
        });
        Assert.True(createResult.ReturnCode.IsSuccess);

        int threadHandle = (int)createResult.ReturnValue1;

        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)threadHandle,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(30UL, result.ReturnValue1);
    }

    [Fact]
    public void GetThreadPriority_InvalidHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = 0x12345678, // 不存在的句柄
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void GetThreadPriority_HandleZero_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = 0,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void GetThreadPriority_NonThreadHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        // 创建一个非 KThread 的内核对象（KClientSession）
        var session = new KClientSession("test:");
        int sessionHandle = _process.HandleTable.CreateHandle(session);

        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)sessionHandle,
        });

        // KClientSession 不是 KThread，GetObject<KThread> 返回 null
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void GetThreadPriority_NoActiveProcess_ReturnsInvalidState()
    {
        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = 0xD000,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SetThreadPriority 测试 ────────────────────

    [Fact]
    public void SetThreadPriority_MainThreadHandle_UpdatesPriority()
    {
        _process = CreateMockProcess();

        // 修改主线程优先级为 20
        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = 20,
        });

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证通过 GetThreadPriority 读回
        var getResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)_process.MainThreadHandle,
        });
        Assert.Equal(20UL, getResult.ReturnValue1);
    }

    [Fact]
    public void SetThreadPriority_PseudoHandle_UpdatesMainThreadPriority()
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = HorizonSystem.CurrentProcessPseudoHandle,
            X1 = 55,
        });

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证通过主线程句柄读回
        var getResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)_process.MainThreadHandle,
        });
        Assert.Equal(55UL, getResult.ReturnValue1);
    }

    [Fact]
    public void SetThreadPriority_CreatedThread_UpdatesPriority()
    {
        _process = CreateMockProcess();

        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = EntryPoint,
            X1 = 0,
            X2 = 0,
            X3 = 44,
            X4 = unchecked((ulong)(long)-2),
        });
        int threadHandle = (int)createResult.ReturnValue1;

        // 修改为优先级 10
        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)threadHandle,
            X1 = 10,
        });

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证读回
        var getResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)threadHandle,
        });
        Assert.Equal(10UL, getResult.ReturnValue1);
    }

    [Fact]
    public void SetThreadPriority_InvalidHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = 0xDEADBEEF,
            X1 = 30,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void SetThreadPriority_HandleZero_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = 0,
            X1 = 30,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void SetThreadPriority_NonThreadHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var session = new KClientSession("test:");
        int sessionHandle = _process.HandleTable.CreateHandle(session);

        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)sessionHandle,
            X1 = 30,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    // ──────────────────── 优先级范围校验测试 ────────────────────

    [Theory]
    [InlineData(0)]   // 最低优先级
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(44)]  // 标准应用线程
    [InlineData(63)]  // 最高优先级
    public void SetThreadPriority_ValidRange_Succeeds(int priority)
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = (ulong)priority,
        });

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证读回一致
        var getResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)_process.MainThreadHandle,
        });
        Assert.Equal((ulong)priority, getResult.ReturnValue1);
    }

    [Theory]
    [InlineData(-1)]   // 低于下限
    [InlineData(64)]   // 超出上限
    [InlineData(100)]  // 远超范围
    [InlineData(-10)]  // 远低于范围
    public void SetThreadPriority_OutOfRange_ReturnsInvalidPriority(int priority)
    {
        _process = CreateMockProcess();

        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = unchecked((ulong)(long)priority),
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidPriority), result.ReturnCode);
    }

    [Fact]
    public void SetThreadPriority_OutOfRange_DoesNotModifyPriority()
    {
        _process = CreateMockProcess();

        // 尝试设置无效优先级
        _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = 100,
        });

        // 原优先级不变
        var getResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)_process.MainThreadHandle,
        });
        Assert.Equal(44UL, getResult.ReturnValue1); // 仍是默认值
    }

    [Fact]
    public void SetThreadPriority_NoActiveProcess_ReturnsInvalidState()
    {
        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = 0xD000,
            X1 = 30,
        });

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 分发集成测试 ────────────────────

    [Fact]
    public void GetThreadPriority_ViaSvcDispatcher_ReturnsSuccess()
    {
        _process = CreateMockProcess();
        _svcDispatcher.Register(0x09, svc => _system.GetThreadPriority(svc));

        var result = _svcDispatcher.Dispatch(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)_process.MainThreadHandle,
        });

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(44UL, result.ReturnValue1);
    }

    [Fact]
    public void SetThreadPriority_ViaSvcDispatcher_ReturnsSuccess()
    {
        _process = CreateMockProcess();
        _svcDispatcher.Register(0x0A, svc => _system.SetThreadPriority(svc));

        var result = _svcDispatcher.Dispatch(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)_process.MainThreadHandle,
            X1 = 25,
        });

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void GetThreadPriority_SvcName_Registered()
    {
        Assert.Equal("GetThreadPriority", _svcDispatcher.GetSvcName(0x09));
    }

    [Fact]
    public void SetThreadPriority_SvcName_Registered()
    {
        Assert.Equal("SetThreadPriority", _svcDispatcher.GetSvcName(0x0A));
    }

    // ──────────────────── Get/Set 往返测试 ────────────────────

    [Fact]
    public void GetSetPriority_RoundTrip_MultipleChanges()
    {
        _process = CreateMockProcess();

        // 连续修改多次，每次读回验证
        int[] priorities = { 0, 63, 32, 44, 16, 47 };

        foreach (var pri in priorities)
        {
            _system.SetThreadPriority(new SvcInfo
            {
                SvcNumber = 0x0A,
                X0 = (ulong)_process.MainThreadHandle,
                X1 = (ulong)pri,
            });

            var result = _system.GetThreadPriority(new SvcInfo
            {
                SvcNumber = 0x09,
                X0 = (ulong)_process.MainThreadHandle,
            });
            Assert.Equal((ulong)pri, result.ReturnValue1);
        }
    }

    [Fact]
    public void GetSetPriority_CreateThreadWithPriority_GetSetWork()
    {
        _process = CreateMockProcess();

        // 创建线程时指定优先级
        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = EntryPoint,
            X1 = 0,
            X2 = 0,
            X3 = 20,
            X4 = unchecked((ulong)(long)-2),
        });
        int handle = (int)createResult.ReturnValue1;

        // GetThreadPriority 应返回 20
        var getResult = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)handle,
        });
        Assert.Equal(20UL, getResult.ReturnValue1);

        // SetThreadPriority 修改为 50
        _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)handle,
            X1 = 50,
        });

        // 读回应为 50
        var getResult2 = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)handle,
        });
        Assert.Equal(50UL, getResult2.ReturnValue1);
    }

    // ──────────────────── Closed handle 测试 ────────────────────

    [Fact]
    public void GetThreadPriority_ClosedHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = EntryPoint,
            X1 = 0,
            X2 = 0,
            X3 = 30,
            X4 = unchecked((ulong)(long)-2),
        });
        int handle = (int)createResult.ReturnValue1;

        // 关闭句柄
        _system.CloseHandle(new SvcInfo { SvcNumber = 0x19, X0 = (ulong)handle });

        // GetThreadPriority 应返回 InvalidHandle
        var result = _system.GetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x09,
            X0 = (ulong)handle,
        });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void SetThreadPriority_ClosedHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();

        var createResult = _system.CreateThread(new SvcInfo
        {
            SvcNumber = 0x34,
            X0 = EntryPoint,
            X1 = 0,
            X2 = 0,
            X3 = 30,
            X4 = unchecked((ulong)(long)-2),
        });
        int handle = (int)createResult.ReturnValue1;

        // 关闭句柄
        _system.CloseHandle(new SvcInfo { SvcNumber = 0x19, X0 = (ulong)handle });

        // SetThreadPriority 应返回 InvalidHandle
        var result = _system.SetThreadPriority(new SvcInfo
        {
            SvcNumber = 0x0A,
            X0 = (ulong)handle,
            X1 = 50,
        });
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    public void Dispose() => _memory.Dispose();
}
