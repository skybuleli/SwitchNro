using System;
using System.Runtime.InteropServices;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using Xunit;

namespace SwitchNro.Tests;

/// <summary>
/// Homebrew ABI loader_config 单元测试
/// 验证 ConfigEntry 结构体布局、构建器方法、内存写入正确性
/// </summary>
public class HomebrewAbiTests : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private const ulong BaseAddress = 0x0008_0000;

    public HomebrewAbiTests()
    {
        _memory = new VirtualMemoryManager();
    }

    public void Dispose() => _memory.Dispose();

    // ──────────────────── 结构体布局测试 ────────────────────

    [Fact]
    public void HbConfigEntry_SizeIs40Bytes()
    {
        // ConfigEntry = Key(4) + Flags(4) + Value[4](8×4) = 40 字节
        Assert.Equal(40, Marshal.SizeOf<HbConfigEntry>());
    }

    [Fact]
    public void HbConfigEntry_FieldsAtCorrectOffsets()
    {
        Assert.Equal(0, Marshal.OffsetOf<HbConfigEntry>(nameof(HbConfigEntry.Key)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<HbConfigEntry>(nameof(HbConfigEntry.Flags)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<HbConfigEntry>(nameof(HbConfigEntry.Value0)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<HbConfigEntry>(nameof(HbConfigEntry.Value1)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<HbConfigEntry>(nameof(HbConfigEntry.Value2)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<HbConfigEntry>(nameof(HbConfigEntry.Value3)).ToInt32());
    }

    // ──────────────────── 构建器基础测试 ────────────────────

    [Fact]
    public void Builder_NoEntries_TotalSizeIsZero()
    {
        var builder = new HomebrewLoaderConfig(_memory);
        Assert.Equal(0, builder.TotalSize);
    }

    [Fact]
    public void Builder_AddMainThreadHandle_IncrementsTotalSize()
    {
        var builder = new HomebrewLoaderConfig(_memory)
            .AddMainThreadHandle(0xD000);
        Assert.Equal(40, builder.TotalSize); // 1 entry × 40 bytes
    }

    [Fact]
    public void Builder_AddMultipleEntries_CorrectTotalSize()
    {
        var builder = new HomebrewLoaderConfig(_memory)
            .AddMainThreadHandle(0xD000)
            .AddHeapSize(0x200000)
            .AddSystemVersion(18, 1, 0);
        Assert.Equal(120, builder.TotalSize); // 3 entries × 40 bytes
    }

    // ──────────────────── 内存写入测试 ────────────────────

    [Fact]
    public void WriteToMemory_EndOfListEntryPresent()
    {
        var builder = new HomebrewLoaderConfig(_memory)
            .AddMainThreadHandle(0xD000);
        builder.WriteToMemory(BaseAddress);

        // 第二个条目（索引 1）应为 EndOfList
        var endEntry = _memory.Read<HbConfigEntry>(BaseAddress + 40);
        Assert.Equal(HbEntryType.EndOfList, endEntry.Key);
        Assert.Equal(0U, endEntry.Flags);
    }

    [Fact]
    public void WriteToMemory_MainThreadHandle_EntryCorrect()
    {
        const int ExpectedHandle = 0xD001;
        var builder = new HomebrewLoaderConfig(_memory)
            .AddMainThreadHandle(ExpectedHandle);
        builder.WriteToMemory(BaseAddress);

        var entry = _memory.Read<HbConfigEntry>(BaseAddress);
        Assert.Equal(HbEntryType.MainThreadHandle, entry.Key);
        Assert.Equal((uint)HbConfigFlags.Mandatory, entry.Flags);
        Assert.Equal((ulong)ExpectedHandle, entry.Value0);
    }

    [Fact]
    public void WriteToMemory_HeapSize_EntryCorrect()
    {
        const ulong ExpectedHeapSize = 0x200000;
        var builder = new HomebrewLoaderConfig(_memory)
            .AddHeapSize(ExpectedHeapSize);
        builder.WriteToMemory(BaseAddress);

        var entry = _memory.Read<HbConfigEntry>(BaseAddress);
        Assert.Equal(HbEntryType.OverrideHeap, entry.Key);
        Assert.Equal((uint)HbConfigFlags.Mandatory, entry.Flags);
        Assert.Equal(ExpectedHeapSize, entry.Value0);
    }

    [Fact]
    public void WriteToMemory_SystemVersion_EncodedCorrectly()
    {
        var builder = new HomebrewLoaderConfig(_memory)
            .AddSystemVersion(18, 1, 0);
        builder.WriteToMemory(BaseAddress);

        var entry = _memory.Read<HbConfigEntry>(BaseAddress);
        Assert.Equal(HbEntryType.HosVersion, entry.Key);
        // 版本编码: (18 << 16) | (1 << 8) | 0 = 0x00120100 (18 = 0x12)
        Assert.Equal(0x00120100UL, entry.Value0);
    }

    [Fact]
    public void WriteToMemory_FullConfig_AllEntriesReadable()
    {
        // 模拟 StartProcess 的完整配置：使用 HandleTable 分配真实主线程句柄
        var handleTable = new HandleTable();
        var mainThread = new KThread(1);
        int mainThreadHandle = handleTable.CreateHandle(mainThread);

        var builder = new HomebrewLoaderConfig(_memory)
            .AddMainThreadHandle(mainThreadHandle)
            .AddHeapSize(0)
            .AddSystemVersion(18, 1, 0);
        ulong configAddr = builder.WriteToMemory(BaseAddress);

        Assert.Equal(BaseAddress, configAddr);
        Assert.Equal(BaseAddress, builder.BaseAddress);

        // 验证所有 3 个条目 + EndOfList
        ulong offset = 0;

        // Entry 0: MainThreadHandle — 使用真实句柄值
        var e0 = _memory.Read<HbConfigEntry>(configAddr + offset); offset += 40;
        Assert.Equal(HbEntryType.MainThreadHandle, e0.Key);
        Assert.Equal((ulong)mainThreadHandle, e0.Value0);

        // Entry 1: OverrideHeap
        var e1 = _memory.Read<HbConfigEntry>(configAddr + offset); offset += 40;
        Assert.Equal(HbEntryType.OverrideHeap, e1.Key);
        Assert.Equal(0UL, e1.Value0);

        // Entry 2: HosVersion
        var e2 = _memory.Read<HbConfigEntry>(configAddr + offset); offset += 40;
        Assert.Equal(HbEntryType.HosVersion, e2.Key);
        Assert.Equal(0x00120100UL, e2.Value0);

        // Entry 3: EndOfList
        var e3 = _memory.Read<HbConfigEntry>(configAddr + offset);
        Assert.Equal(HbEntryType.EndOfList, e3.Key);
    }

    // ──────────────────── 系统版本编码测试 ────────────────────

    [Theory]
    [InlineData(9, 0, 0, 0x00090000UL)]    // Horizon 9.0.0
    [InlineData(12, 1, 0, 0x000C0100UL)]   // Horizon 12.1.0
    [InlineData(18, 1, 0, 0x00120100UL)]   // Horizon 18.1.0
    [InlineData(19, 0, 3, 0x00130003UL)]   // Horizon 19.0.3
    public void SystemVersion_Encoding_IsCorrect(byte major, byte minor, byte micro, ulong expected)
    {
        var builder = new HomebrewLoaderConfig(_memory)
            .AddSystemVersion(major, minor, micro);
        builder.WriteToMemory(BaseAddress);

        var entry = _memory.Read<HbConfigEntry>(BaseAddress);
        Assert.Equal(expected, entry.Value0);
    }
}
