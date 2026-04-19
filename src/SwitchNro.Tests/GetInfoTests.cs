using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

public class GetInfoTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    public GetInfoTests()
    {
        _system = new HorizonSystem(_memory, _svcDispatcher);
    }

    private HorizonProcess CreateMockProcess(ulong titleId = 0x0100_0000_0000_0000)
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

        _memory.MapZero(nroModule.TextSegment.Address, nroModule.TextSegment.Size, MemoryPermissions.ReadExecute);
        _memory.MapZero(nroModule.RodataSegment.Address, nroModule.RodataSegment.Size, MemoryPermissions.Read);
        _memory.MapZero(nroModule.DataSegment.Address, nroModule.DataSegment.Size, MemoryPermissions.ReadWrite);
        _memory.MapZero(nroModule.BssSegment.Address, nroModule.BssSegment.Size, MemoryPermissions.ReadWrite);

        var processInfo = new ProcessInfo
        {
            Name = "TestProcess",
            EntryPoint = nroModule.EntryPoint,
            TitleId = titleId,
        };

        _process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(_process);
        return _process;
    }

    private SvcResult CallGetInfo(InfoType type, ulong handle = 0xFFFF8001, ulong subType = 0)
    {
        var svc = new SvcInfo { SvcNumber = 0x29, X1 = (ulong)type, X2 = handle, X3 = subType };
        return _system.GetInfo(svc);
    }

    // ──────────────────── 进程句柄类 InfoType (0-7, 12-18, 20-23) ────────────────────

    [Fact]
    public void GetInfo_CoreMask_ReturnsCore0()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.CoreMask);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(1UL, result.ReturnValue1); // 仅核心 0
    }

    [Fact]
    public void GetInfo_PriorityMask_ReturnsValidMask()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.PriorityMask);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0x3FUL, result.ReturnValue1); // 优先级 0-59
    }

    [Fact]
    public void GetInfo_AliasRegionAddress_ReturnsAliasBase()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.AliasRegionAddress);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.AliasBase, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_AliasRegionSize_ReturnsAliasSize()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.AliasRegionSize);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.AliasSize, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_HeapRegionAddress_ReturnsCurrentHeapBase()
    {
        CreateMockProcess();
        // 堆未分配时应返回 0
        var result = CallGetInfo(InfoType.HeapRegionAddress);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1); // 未分配堆

        // 分配堆后再查
        _system.SetHeapSize(new SvcInfo { SvcNumber = 0x01, X1 = 0x1000 });
        result = CallGetInfo(InfoType.HeapRegionAddress);
        Assert.Equal(HorizonSystem.HeapBase, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_HeapRegionSize_ReturnsCurrentHeapSize()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.HeapRegionSize);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1); // 未分配堆

        _system.SetHeapSize(new SvcInfo { SvcNumber = 0x01, X1 = 0x2000 });
        result = CallGetInfo(InfoType.HeapRegionSize);
        Assert.Equal(0x2000UL, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_TotalMemorySize_IncludesCodeStackHeap()
    {
        CreateMockProcess();
        _system.SetHeapSize(new SvcInfo { SvcNumber = 0x01, X1 = 0x10000 });

        var result = CallGetInfo(InfoType.TotalMemorySize);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.True(result.ReturnValue1 > 0);

        // 验证: 至少包含栈(1MB) + 堆(64KB) + 代码段(16KB) + TLS(0x200)
        Assert.True(result.ReturnValue1 >= 0x100000 + 0x10000 + 0x4000 + 0x200);
    }

    [Fact]
    public void GetInfo_UsedMemorySize_IncludesCodeStackHeap()
    {
        CreateMockProcess();
        _system.SetHeapSize(new SvcInfo { SvcNumber = 0x01, X1 = 0x10000 });

        var result = CallGetInfo(InfoType.UsedMemorySize);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.True(result.ReturnValue1 > 0);
    }

    [Fact]
    public void GetInfo_AslrRegionAddress_ReturnsAslrBase()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.AslrRegionAddress);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.AslrBase, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_AslrRegionSize_ReturnsAslrSize()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.AslrRegionSize);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.AslrSize, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_StackRegionAddress_ReturnsStackBase()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.StackRegionAddress);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(HorizonSystem.StackBase, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_StackRegionSize_ReturnsMainStackSize()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.StackRegionSize);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0x100000UL, result.ReturnValue1); // 默认 1MB
    }

    [Fact]
    public void GetInfo_SystemResourceSizeTotal_ReturnsNonZero()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.SystemResourceSizeTotal);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0x200000UL, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_ProgramId_ReturnsTitleId()
    {
        const ulong testTitleId = 0x0100_ABCD_0001_0000;
        CreateMockProcess(testTitleId);
        var result = CallGetInfo(InfoType.ProgramId);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(testTitleId, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_IsApplication_Returns1ForApplication()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.IsApplication);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(1UL, result.ReturnValue1); // 默认类别是 Application
    }

    [Fact]
    public void GetInfo_UserExceptionContextAddress_ReturnsZero()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.UserExceptionContextAddress);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1);
    }

    // ──────────────────── 零句柄类 InfoType (8-11, 19) ────────────────────

    [Fact]
    public void GetInfo_DebuggerAttached_ReturnsZero()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.DebuggerAttached, handle: 0);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1); // 无调试器
    }

    [Fact]
    public void GetInfo_ResourceLimit_ReturnsPseudoHandle()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.ResourceLimit, handle: 0);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0xFFFF8001UL, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_IdleTickCount_ReturnsZero()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.IdleTickCount, handle: 0);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_RandomEntropy_ReturnsNonZero()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.RandomEntropy, handle: 0, subType: 0);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.NotEqual(0UL, result.ReturnValue1); // 应该有随机值

        // 不同 subType 应返回不同值
        var result2 = CallGetInfo(InfoType.RandomEntropy, handle: 0, subType: 1);
        Assert.NotEqual(result.ReturnValue1, result2.ReturnValue1);
    }

    [Fact]
    public void GetInfo_RandomEntropy_SubTypeAbove3_ReturnsZero()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.RandomEntropy, handle: 0, subType: 4);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0UL, result.ReturnValue1); // subType > 3 无效
    }

    [Fact]
    public void GetInfo_InitialProcessIdRange_SubType0_ReturnsLowerBound()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.InitialProcessIdRange, handle: 0, subType: 0);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(1UL, result.ReturnValue1);
    }

    [Fact]
    public void GetInfo_InitialProcessIdRange_SubType1_ReturnsUpperBound()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.InitialProcessIdRange, handle: 0, subType: 1);
        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0x50UL, result.ReturnValue1);
    }

    // ──────────────────── 句柄验证 ────────────────────

    [Fact]
    public void GetInfo_ProcessType_WithZeroHandle_ReturnsInvalidHandle()
    {
        // handle=0 对进程类型 InfoType 不是有效句柄
        CreateMockProcess();
        var result = CallGetInfo(InfoType.CoreMask, handle: 0);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void GetInfo_ProcessType_WithInvalidHandle_ReturnsInvalidHandle()
    {
        CreateMockProcess();
        var svc = new SvcInfo { SvcNumber = 0x29, X1 = (ulong)InfoType.CoreMask, X2 = 0x1234, X3 = 0 };
        var result = _system.GetInfo(svc);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void GetInfo_ProcessType_WithPseudoHandle_ReturnsSuccess()
    {
        CreateMockProcess();
        var result = CallGetInfo(InfoType.CoreMask, handle: 0xFFFF8000);
        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void GetInfo_ZeroHandleType_WithInvalidHandle_ReturnsInvalidHandle()
    {
        CreateMockProcess();
        // DebuggerAttached 需要零句柄，传无效句柄应报错
        var result = CallGetInfo(InfoType.DebuggerAttached, handle: 0xDEAD);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void GetInfo_NoActiveProcess_ReturnsInvalidState()
    {
        // 不创建进程，使用伪句柄
        var svc = new SvcInfo { SvcNumber = 0x29, X1 = (ulong)InfoType.CoreMask, X2 = 0xFFFF8000, X3 = 0 };
        var result = _system.GetInfo(svc);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void GetInfo_UnknownInfoType_ReturnsInvalidState()
    {
        CreateMockProcess();
        // InfoType 99 不存在
        var svc = new SvcInfo { SvcNumber = 0x29, X1 = 99, X2 = 0xFFFF8000, X3 = 0 };
        var result = _system.GetInfo(svc);
        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void GetInfo_Svc0x28_SameAsSvc0x29()
    {
        // 真实 Horizon 中 SVC 0x28 和 0x29 均为 GetInfo
        CreateMockProcess();
        var svc28 = new SvcInfo { SvcNumber = 0x28, X1 = (ulong)InfoType.CoreMask, X2 = 0xFFFF8000, X3 = 0 };
        var svc29 = new SvcInfo { SvcNumber = 0x29, X1 = (ulong)InfoType.CoreMask, X2 = 0xFFFF8000, X3 = 0 };
        var result28 = _system.GetInfo(svc28);
        var result29 = _system.GetInfo(svc29);
        Assert.Equal(result29.ReturnCode, result28.ReturnCode);
        Assert.Equal(result29.ReturnValue1, result28.ReturnValue1);
    }

    // ──────────────────── 非系统内存 ────────────────────

    [Fact]
    public void GetInfo_TotalNonSystemMemorySize_LessThanTotalMemory()
    {
        CreateMockProcess();
        _system.SetHeapSize(new SvcInfo { SvcNumber = 0x01, X1 = 0x10000 });

        var total = CallGetInfo(InfoType.TotalMemorySize).ReturnValue1;
        var nonSystem = CallGetInfo(InfoType.TotalNonSystemMemorySize).ReturnValue1;
        Assert.True(nonSystem < total);
    }

    [Fact]
    public void GetInfo_UsedNonSystemMemorySize_LessThanUsedMemory()
    {
        CreateMockProcess();
        _system.SetHeapSize(new SvcInfo { SvcNumber = 0x01, X1 = 0x10000 });

        var used = CallGetInfo(InfoType.UsedMemorySize).ReturnValue1;
        var nonSystem = CallGetInfo(InfoType.UsedNonSystemMemorySize).ReturnValue1;
        Assert.True(nonSystem < used);
    }

    public void Dispose() => _memory.Dispose();
}
