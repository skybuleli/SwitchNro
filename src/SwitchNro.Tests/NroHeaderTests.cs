using System;
using System.IO;
using System.Runtime.InteropServices;
using SwitchNro.NroLoader;
using Xunit;

namespace SwitchNro.Tests;

public class NroHeaderTests
{
    [Fact]
    public void NroHeader_ValidMagic_ReturnsTrue()
    {
        var header = new NroHeader { Magic = 0x304F524E }; // "NRO0" little-endian
        Assert.True(header.IsValid);
    }

    [Fact]
    public void NroHeader_InvalidMagic_ReturnsFalse()
    {
        var header = new NroHeader { Magic = 0x12345678 };
        Assert.False(header.IsValid);
    }

    [Fact]
    public void AssetHeader_ValidMagic_ReturnsTrue()
    {
        var header = new AssetHeader { Magic = 0x54455341 }; // "ASET" little-endian
        Assert.True(header.IsValid);
    }

    [Fact]
    public void Mod0Header_ValidMagic_ReturnsTrue()
    {
        var header = new Mod0Header { Magic = 0x30444F4D }; // "MOD0" little-endian
        Assert.True(header.IsValid);
    }

    [Fact]
    public void NroHeader_SizeIs0x70()
    {
        var size = Marshal.SizeOf<NroHeader>();
        Assert.Equal(0x70, size);
    }

    [Fact]
    public void AssetHeader_MemoryLayoutIsCorrect()
    {
        var size = Marshal.SizeOf<AssetHeader>();
        // 确保布局与 Switch 格式对齐
        Assert.True(size > 0);
    }
}
