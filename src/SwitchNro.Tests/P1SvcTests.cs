using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Tests.TestUtilities;
using Xunit;

namespace SwitchNro.Tests;

public class P1SvcTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();
    private readonly SvcDispatcher _svcDispatcher = new();
    private readonly HorizonSystem _system;
    private HorizonProcess? _process;

    // 测试用地址（必须在 TLS 区域 0x0100_0000~0x0100_8000 之后，
    // 否则 StartProcess 中 MapZero(TLS) 会与测试区域的 MapZero 冲突）
    private const ulong TestRegionBase = 0x0100_9000;
    private const ulong PageSize = 0x1000;
    private const ulong MutexAddr = 0x0100_9000;       // 互斥锁地址
    private const ulong FutexAddr = 0x0100_A000;       // futex 等待地址
    private const ulong FutexKey = 0x0100_B000;        // process_wide_key

    public P1SvcTests()
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
                Magic = 0, Version = 0,
                TextSize = 0x1000, RodataSize = 0x1000,
                DataSize = 0x1000, BssSize = 0x1000,
            },
            TextSegment = new SegmentInfo(0x0800_0000, 0, 0x1000),
            RodataSegment = new SegmentInfo(0x0801_0000, 0, 0x1000),
            DataSegment = new SegmentInfo(0x0802_0000, 0, 0x1000),
            BssSegment = new SegmentInfo(0x0803_0000, 0, 0x1000),
        };

        // 仅在页面未映射时才映射（支持同一测试内创建多个进程的场景）
        MapIfNotMapped(nroModule.TextSegment.Address, nroModule.TextSegment.Size, MemoryPermissions.ReadExecute, MemoryType.CodeStatic);
        MapIfNotMapped(nroModule.RodataSegment.Address, nroModule.RodataSegment.Size, MemoryPermissions.Read, MemoryType.CodeStatic);
        MapIfNotMapped(nroModule.DataSegment.Address, nroModule.DataSegment.Size, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);
        MapIfNotMapped(nroModule.BssSegment.Address, nroModule.BssSegment.Size, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);

        // 映射测试用的内存区域（互斥锁、futex 地址）
        MapIfNotMapped(TestRegionBase, PageSize * 4, MemoryPermissions.ReadWrite, MemoryType.Heap);

        var processInfo = new ProcessInfo { Name = "P1SvcTest", EntryPoint = nroModule.EntryPoint };
        var process = _system.CreateProcess(nroModule, processInfo, new StubExecutionEngine());
        _system.StartProcess(process);
        return process;
    }

    // ──────────────────── SVC 0x0F ArbitrateLock ────────────────────

    [Fact]
    public void ArbitrateLock_UnlockedMutex_AcquiresSuccessfully()
    {
        _process = CreateMockProcess();

        // 初始化互斥锁为 0（未锁定）
        _memory.Write(MutexAddr, BitConverter.GetBytes(0U));

        var svc = new SvcInfo { SvcNumber = 0x0F, X0 = HorizonSystem.CurrentProcessPseudoHandle, X1 = MutexAddr, X2 = 0x4242 };
        var result = _system.ArbitrateLock(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        // 验证 guest 内存中的值已被写入 tag
        Assert.Equal(0x4242U, _memory.Read<uint>(MutexAddr));
    }

    [Fact]
    public void ArbitrateLock_LockedMutex_ReturnsConcurrentConflict()
    {
        _process = CreateMockProcess();

        // 初始化互斥锁为非零值（已锁定）
        _memory.Write(MutexAddr, BitConverter.GetBytes(0x1234U));

        var svc = new SvcInfo { SvcNumber = 0x0F, X0 = HorizonSystem.CurrentProcessPseudoHandle, X1 = MutexAddr, X2 = 0x4242 };
        var result = _system.ArbitrateLock(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.ConcurrentConflict), result.ReturnCode);
    }

    [Fact]
    public void ArbitrateLock_ThenUnlock_ThenLockAgain_Succeeds()
    {
        _process = CreateMockProcess();

        _memory.Write(MutexAddr, BitConverter.GetBytes(0U));

        // 获取锁
        var lockSvc = new SvcInfo { SvcNumber = 0x0F, X0 = HorizonSystem.CurrentProcessPseudoHandle, X1 = MutexAddr, X2 = 0xABCD };
        var lockResult = _system.ArbitrateLock(lockSvc);
        Assert.True(lockResult.ReturnCode.IsSuccess);
        Assert.Equal(0xABCDU, _memory.Read<uint>(MutexAddr));

        // 释放锁（地址在 X1，不是 X0）
        var unlockSvc = new SvcInfo { SvcNumber = 0x10, X1 = MutexAddr };
        var unlockResult = _system.ArbitrateUnlock(unlockSvc);
        Assert.True(unlockResult.ReturnCode.IsSuccess);
        Assert.Equal(0U, _memory.Read<uint>(MutexAddr));

        // 再次获取锁
        var lockAgainSvc = new SvcInfo { SvcNumber = 0x0F, X0 = HorizonSystem.CurrentProcessPseudoHandle, X1 = MutexAddr, X2 = 0xEF01 };
        var lockAgainResult = _system.ArbitrateLock(lockAgainSvc);
        Assert.True(lockAgainResult.ReturnCode.IsSuccess);
        Assert.Equal(0xEF01U, _memory.Read<uint>(MutexAddr));
    }

    [Fact]
    public void ArbitrateLock_InvalidHandle_ReturnsInvalidHandle()
    {
        _process = CreateMockProcess();
        _memory.Write(MutexAddr, BitConverter.GetBytes(0U));

        var svc = new SvcInfo { SvcNumber = 0x0F, X0 = 0xDEAD, X1 = MutexAddr, X2 = 0x4242 };
        var result = _system.ArbitrateLock(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidHandle), result.ReturnCode);
    }

    [Fact]
    public void ArbitrateLock_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x0F, X0 = 0, X1 = MutexAddr, X2 = 0 };
        var result = _system.ArbitrateLock(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    // ──────────────────── SVC 0x10 ArbitrateUnlock ────────────────────

    [Fact]
    public void ArbitrateUnlock_SetsMutexToZero()
    {
        _process = CreateMockProcess();

        // 设置互斥锁为已锁定状态
        _memory.Write(MutexAddr, BitConverter.GetBytes(0xABCDU));

        var svc = new SvcInfo { SvcNumber = 0x10, X1 = MutexAddr };
        var result = _system.ArbitrateUnlock(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0U, _memory.Read<uint>(MutexAddr));
    }

    [Fact]
    public void ArbitrateUnlock_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x10, X1 = MutexAddr };
        var result = _system.ArbitrateUnlock(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void ArbitrateUnlock_InvalidAddress_ReturnsInvalidAddress()
    {
        _process = CreateMockProcess();

        // 使用超出进程地址空间的地址
        var svc = new SvcInfo { SvcNumber = 0x10, X1 = 0xDEAD_BEEF };
        var result = _system.ArbitrateUnlock(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    // ──────────────────── SVC 0x11 WaitProcessWideKeyAtomic ────────────────────

    [Fact]
    public void WaitProcessWideKeyAtomic_ValueChanged_ReturnsSuccessImmediately()
    {
        _process = CreateMockProcess();

        // 设置地址值为 0，期望 tag = 1 → 值不匹配，无需等待
        _memory.Write(FutexAddr, BitConverter.GetBytes(0U));

        var svc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 1, X3 = unchecked((ulong)(-1L)) };
        var result = _system.WaitProcessWideKeyAtomic(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void WaitProcessWideKeyAtomic_ValueMatches_ZeroTimeout_ReturnsTimedOut()
    {
        _process = CreateMockProcess();

        // 设置地址值为 1，期望 tag = 1 → 值匹配，需要等待
        _memory.Write(FutexAddr, BitConverter.GetBytes(1U));

        var svc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 1, X3 = 0 };
        var result = _system.WaitProcessWideKeyAtomic(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    [Fact]
    public void WaitProcessWideKeyAtomic_SignalThenWait_ReturnsSuccess()
    {
        _process = CreateMockProcess();

        // 设置地址值为 1，期望 tag = 1
        _memory.Write(FutexAddr, BitConverter.GetBytes(1U));

        // 先发送信号
        var signalSvc = new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 1 };
        _system.SignalProcessWideKey(signalSvc);

        // 然后等待（短超时，应检测到信号）
        var waitSvc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 1, X3 = 10_000_000UL };
        var result = _system.WaitProcessWideKeyAtomic(waitSvc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void WaitProcessWideKeyAtomic_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 0, X3 = 0 };
        var result = _system.WaitProcessWideKeyAtomic(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void WaitProcessWideKeyAtomic_ValueChangesDuringWait_ReturnsSuccess()
    {
        _process = CreateMockProcess();

        // 设置地址值为 5，期望 tag = 5
        _memory.Write(FutexAddr, BitConverter.GetBytes(5U));

        // 在等待期间修改值（模拟另一个线程的 SignalAndModify）
        // 由于是同线程测试，我们先修改值，再调用等待
        _memory.Write(FutexAddr, BitConverter.GetBytes(0U)); // 值已变

        var svc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 5, X3 = 10_000_000UL };
        var result = _system.WaitProcessWideKeyAtomic(svc);

        Assert.True(result.ReturnCode.IsSuccess); // 值已变，立即返回
    }

    // ──────────────────── SVC 0x12 SignalProcessWideKey ────────────────────

    [Fact]
    public void SignalProcessWideKey_PositiveCount_ReturnsSuccess()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 1 };
        var result = _system.SignalProcessWideKey(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SignalProcessWideKey_CountMinusOne_WakesAll()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = unchecked((ulong)(-1L)) };
        var result = _system.SignalProcessWideKey(svc);

        Assert.True(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SignalProcessWideKey_CountZero_ReturnsInvalidCount()
    {
        _process = CreateMockProcess();

        var svc = new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 0 };
        var result = _system.SignalProcessWideKey(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidCount), result.ReturnCode);
    }

    [Fact]
    public void SignalProcessWideKey_NoActiveProcess_ReturnsInvalidState()
    {
        var svc = new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 1 };
        var result = _system.SignalProcessWideKey(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);
    }

    [Fact]
    public void SignalProcessWideKey_MultipleSignals_Accumulate()
    {
        _process = CreateMockProcess();

        // 第一次 signal count=2
        _system.SignalProcessWideKey(new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 2 });
        // 第二次 signal count=3
        _system.SignalProcessWideKey(new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 3 });

        // 设置地址值匹配，然后等待
        _memory.Write(FutexAddr, BitConverter.GetBytes(42U));

        // 第一次等待应成功（消耗 1 个信号）
        var wait1 = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 42, X3 = 10_000_000UL };
        var result1 = _system.WaitProcessWideKeyAtomic(wait1);
        Assert.True(result1.ReturnCode.IsSuccess);

        // 重置地址值以允许第二次等待
        _memory.Write(FutexAddr, BitConverter.GetBytes(42U));

        // 第二次等待也应成功（还有 4 个信号剩余）
        var result2 = _system.WaitProcessWideKeyAtomic(wait1);
        Assert.True(result2.ReturnCode.IsSuccess);
    }

    // ──────────────────── Process cleanup ────────────────────

    [Fact]
    public void ExitProcess_ClearsFutexSignals_NoStaleStateForNextProcess()
    {
        // 进程 1：发送 futex 信号后退出
        _process = CreateMockProcess();
        _system.SignalProcessWideKey(new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 5 });
        _system.ExitProcess(new SvcInfo { SvcNumber = 0x06, X0 = 0 });

        // 进程 2：等待同一个 key — 应超时，不应继承进程 1 的信号
        _process = CreateMockProcess();
        _memory.Write(FutexAddr, BitConverter.GetBytes(1U)); // 值匹配 tag
        var waitSvc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 1, X3 = 0 };
        var result = _system.WaitProcessWideKeyAtomic(waitSvc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    // ──────────────────── CancelSynchronization + WaitProcessWideKeyAtomic ────────────────────

    [Fact]
    public void CancelSynchronization_AffectsWaitProcessWideKeyAtomic()
    {
        _process = CreateMockProcess();

        // 设置取消标志
        _system.CancelSynchronization(new SvcInfo { SvcNumber = 0x0E, X0 = HorizonSystem.CurrentProcessPseudoHandle });

        // WaitProcessWideKeyAtomic 应在轮询期间检测到取消标志
        _memory.Write(FutexAddr, BitConverter.GetBytes(1U)); // 值匹配 tag
        var waitSvc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 1, X3 = 10_000_000UL };
        var result = _system.WaitProcessWideKeyAtomic(waitSvc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.WaitSyncCancelled), result.ReturnCode);
        Assert.False(_process.SyncCancelRequested); // 标志已清除
    }

    [Fact]
    public void CancelSynchronization_DoesNotAffect_ValueChangedWaitProcessWideKeyAtomic()
    {
        _process = CreateMockProcess();

        // 设置取消标志
        _system.CancelSynchronization(new SvcInfo { SvcNumber = 0x0E, X0 = HorizonSystem.CurrentProcessPseudoHandle });

        // WaitProcessWideKeyAtomic: 值不匹配 → 立即返回 Success，取消标志不影响
        _memory.Write(FutexAddr, BitConverter.GetBytes(0U)); // 值 != tag
        var waitSvc = new SvcInfo { SvcNumber = 0x11, X0 = FutexAddr, X1 = FutexKey, X2 = 1, X3 = 10_000_000UL };
        var result = _system.WaitProcessWideKeyAtomic(waitSvc);

        Assert.True(result.ReturnCode.IsSuccess); // 值不匹配，立即返回
        Assert.True(_process.SyncCancelRequested); // 取消标志未被消耗
    }

    // ──────────────────── Integration: Lock/Unlock + ProcessWideKey ────────────────────

    [Fact]
    public void Integration_MutexLockUnlock_WithProcessWideKeySignal()
    {
        _process = CreateMockProcess();

        // 场景：用户态互斥锁 + futex 配合使用
        // 1. 获取锁
        _memory.Write(MutexAddr, BitConverter.GetBytes(0U));
        var lockResult = _system.ArbitrateLock(new SvcInfo { SvcNumber = 0x0F, X0 = HorizonSystem.CurrentProcessPseudoHandle, X1 = MutexAddr, X2 = 0x100 });
        Assert.True(lockResult.ReturnCode.IsSuccess);

        // 2. 临界区操作...

        // 3. 释放锁
        var unlockResult = _system.ArbitrateUnlock(new SvcInfo { SvcNumber = 0x10, X1 = MutexAddr });
        Assert.True(unlockResult.ReturnCode.IsSuccess);

        // 4. 通知其他等待线程（SignalProcessWideKey）
        var signalResult = _system.SignalProcessWideKey(new SvcInfo { SvcNumber = 0x12, X0 = FutexKey, X1 = 1 });
        Assert.True(signalResult.ReturnCode.IsSuccess);

        // 5. 验证锁已释放
        Assert.Equal(0U, _memory.Read<uint>(MutexAddr));
    }

    /// <summary>
    /// 仅在页面未映射时才调用 MapZero，避免 MemoryAlreadyMappedException
    /// （支持同一测试内多次调用 CreateMockProcess 的场景）
    /// </summary>
    private void MapIfNotMapped(ulong addr, ulong size, MemoryPermissions perms, MemoryType type = MemoryType.Normal)
    {
        if (!_memory.IsMapped(addr))
            _memory.MapZero(addr, size, perms, type);
    }

    public void Dispose() => _memory.Dispose();
}
