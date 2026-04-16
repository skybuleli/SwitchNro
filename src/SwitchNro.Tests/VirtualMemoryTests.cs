using System;
using SwitchNro.Memory;
using Xunit;

namespace SwitchNro.Tests;

public class VirtualMemoryManagerTests : IDisposable
{
    private readonly VirtualMemoryManager _memory = new();

    [Fact]
    public void MapAndRead_ByteData_ReturnsSameData()
    {
        // 准备
        var data = new byte[] { 0x42, 0x43, 0x44, 0x45 };
        _memory.Map(0x1000, data, MemoryPermissions.ReadWrite);

        // 执行
        var result = new byte[4];
        _memory.Read(0x1000, result);

        // 验证
        Assert.Equal(data, result);
    }

    [Fact]
    public void WriteAndRead_GenericMethod_ReturnsSameValue()
    {
        _memory.MapZero(0x2000, 0x1000, MemoryPermissions.ReadWrite);

        _memory.Write(0x2000, 0xDEADBEEFUL);
        var result = _memory.Read<ulong>(0x2000);

        Assert.Equal(0xDEADBEEFUL, result);
    }

    [Fact]
    public void MapZero_RegionIsZeroed()
    {
        _memory.MapZero(0x3000, 0x2000, MemoryPermissions.ReadWrite);

        var result = new byte[16];
        _memory.Read(0x3000, result);

        Assert.All(result, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Unmap_AccessThrowsException()
    {
        var data = new byte[] { 1, 2, 3 };
        _memory.Map(0x4000, data, MemoryPermissions.ReadWrite);
        _memory.Unmap(0x4000, 0x1000);

        Assert.Throws<MemoryAccessException>(() =>
        {
            var buf = new byte[1];
            _memory.Read(0x4000, buf);
        });
    }

    [Fact]
    public void WritePermissions_ReadOnlyThrowsException()
    {
        var data = new byte[] { 1, 2, 3 };
        _memory.Map(0x5000, data, MemoryPermissions.Read);

        Assert.Throws<MemoryAccessException>(() =>
        {
            _memory.Write(0x5000, new byte[] { 0xFF });
        });
    }

    [Fact]
    public void HandlePageFault_UnmappedAddr_AllocatesNewPage()
    {
        // 映射一个大范围但只部分使用
        _memory.HandlePageFault(0x6000, MemoryPermissions.ReadWrite);

        // 写入应该成功
        _memory.Write(0x6000, 42);
        var result = _memory.Read<int>(0x6000);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ResidentSize_IncreasesAfterMapping()
    {
        var initialSize = _memory.ResidentSize;
        _memory.MapZero(0x7000, 0x3000, MemoryPermissions.ReadWrite);

        Assert.True(_memory.ResidentSize > initialSize);
    }

    public void Dispose() => _memory.Dispose();
}
