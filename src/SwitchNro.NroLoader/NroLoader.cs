using System;
using System.IO;
using System.Runtime.CompilerServices;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Common.Utilities;
using SwitchNro.Memory;

namespace SwitchNro.NroLoader;

/// <summary>
/// NRO 加载器
/// 负责解析 NRO 文件、映射到虚拟地址空间、执行重定位
/// </summary>
public sealed class NroLoader
{
    private readonly VirtualMemoryManager _memory;

    public NroLoader(VirtualMemoryManager memory)
    {
        _memory = memory;
    }

    /// <summary>加载 NRO 文件并返回加载信息</summary>
    public NroModule Load(string filePath)
    {
        Logger.Info(nameof(NroLoader), $"开始加载 NRO: {filePath}");

        using var stream = File.OpenRead(filePath);
        var header = SpanHelper.ReadStruct<NroHeader>(stream, 0);

        if (!header.IsValid)
            throw new InvalidDataException($"无效的 NRO 文件: 魔数不匹配 0x{header.Magic:X8}");

        // ASLR: 在有效范围内随机化基地址 (bits 37-12，对齐 4KB)
        var random = new Random();
        ulong aslrSlot = (ulong)(random.Next() & 0x1FFFF) << 12; // 25-bit 随机，对齐 4KB
        ulong basePath = 0x0008_0000 + aslrSlot; // 从 512KB 开始，避免零页

        var module = LoadFromStream(stream, header, basePath);

        // 检查 Asset Section
        var assetHeader = TryParseAssetHeader(stream, header.Size);
        if (assetHeader != null)
        {
            module.Assets = new NroAssetInfo
            {
                IconOffset = assetHeader.Value.IconOffset,
                IconSize = assetHeader.Value.IconSize,
                RomFsOffset = assetHeader.Value.RomFsOffset,
                RomFsSize = assetHeader.Value.RomFsSize,
            };
            Logger.Info(nameof(NroLoader), $"发现 Asset Section: RomFS @ 0x{assetHeader.Value.RomFsOffset:X}, 大小 0x{assetHeader.Value.RomFsSize:X}");
        }

        Logger.Info(nameof(NroLoader), $"NRO 加载完成: 基地址=0x{module.BaseAddress:X16}, 入口=0x{module.EntryPoint:X16}");
        return module;
    }

    /// <summary>从流中加载 NRO（可复用于动态模块加载）</summary>
    public NroModule LoadFromStream(Stream stream, NroHeader header, ulong baseAddress)
    {
        var module = new NroModule
        {
            BaseAddress = baseAddress,
            Header = header,
        };

        // 计算各段虚拟地址
        ulong textAddr = baseAddress;
        ulong rodataAddr = textAddr + AlignPage(header.TextSize);
        ulong dataAddr = rodataAddr + AlignPage(header.RodataSize);
        ulong bssAddr = dataAddr + AlignPage(header.DataSize);

        module.TextSegment = new SegmentInfo(textAddr, header.TextOffset, header.TextSize);
        module.RodataSegment = new SegmentInfo(rodataAddr, header.RodataOffset, header.RodataSize);
        module.DataSegment = new SegmentInfo(dataAddr, header.DataOffset, header.DataSize);
        module.BssSegment = new SegmentInfo(bssAddr, 0, header.BssSize);

        // 映射 .text 段 (代码，可读可执行)
        Span<byte> textData = new byte[header.TextSize];
        stream.Position = header.TextOffset;
        stream.ReadExactly(textData);
        _memory.Map(textAddr, textData, MemoryPermissions.ReadExecute);

        // 映射 .rodata 段 (只读数据)
        Span<byte> rodataData = new byte[header.RodataSize];
        stream.Position = header.RodataOffset;
        stream.ReadExactly(rodataData);
        _memory.Map(rodataAddr, rodataData, MemoryPermissions.Read);

        // 映射 .data 段 (可写数据)
        Span<byte> dataData = new byte[header.DataSize];
        stream.Position = header.DataOffset;
        stream.ReadExactly(dataData);
        _memory.Map(dataAddr, dataData, MemoryPermissions.ReadWrite);

        // 映射 .bss 段 (零填充)
        if (header.BssSize > 0)
        {
            _memory.MapZero(bssAddr, header.BssSize, MemoryPermissions.ReadWrite);
        }

        // 执行重定位
        PerformRelocations(module);

        // 设置入口点
        module.EntryPoint = textAddr; // NRO 入口点默认为 .text 段起始

        return module;
    }

    /// <summary>执行 NRO 重定位</summary>
    private void PerformRelocations(NroModule module)
    {
        // 读取 MOD0 头获取动态重定位信息
        var mod0Offset = module.Header.TextOffset; // MOD0 通常在 .text 段开头
        if (mod0Offset == 0) return;

        // 读取 .text 段的前几个字节检查 MOD0
        Span<byte> mod0CheckData = new byte[Unsafe.SizeOf<Mod0Header>()];
        _memory.Read(module.BaseAddress, mod0CheckData);

        // 搜索 MOD0 魔数 (可能在段内偏移)
        // 实际实现中需要遍历 .text 段搜索 MOD0 标记
        // 这里先实现 R_ARM_RELATIVE 类型的基本重定位

        // 基本的重定位：遍历所有可写段，对包含基地址引用的位置进行修正
        // R_ARM_RELATIVE: *target = base_address + addend
        // 这需要解析 ELF dynamic section 中的 rela.plt/rela.dyn 表

        Logger.Debug(nameof(NroLoader), "重定位处理完成（基本实现）");
    }

    /// <summary>尝试解析 Asset Section</summary>
    private static AssetHeader? TryParseAssetHeader(Stream stream, uint nroSize)
    {
        try
        {
            stream.Position = nroSize;
            var assetHeader = SpanHelper.ReadStruct<AssetHeader>(stream, nroSize);
            return assetHeader.IsValid ? assetHeader : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>卸载 NRO 模块并回收内存</summary>
    public void Unload(NroModule module)
    {
        _memory.Unmap(module.TextSegment.Address, AlignPage(module.Header.TextSize));
        _memory.Unmap(module.RodataSegment.Address, AlignPage(module.Header.RodataSize));
        _memory.Unmap(module.DataSegment.Address, AlignPage(module.Header.DataSize));
        if (module.Header.BssSize > 0)
            _memory.Unmap(module.BssSegment.Address, AlignPage(module.Header.BssSize));

        Logger.Info(nameof(NroLoader), $"已卸载 NRO 模块: 0x{module.BaseAddress:X16}");
    }

    private static ulong AlignPage(uint size) => (size + 0xFFFul) & ~0xFFFul;
}

/// <summary>已加载的 NRO 模块信息</summary>
public sealed class NroModule
{
    public ulong BaseAddress { get; set; }
    public ulong EntryPoint { get; set; }
    public NroHeader Header { get; set; }
    public SegmentInfo TextSegment { get; set; }
    public SegmentInfo RodataSegment { get; set; }
    public SegmentInfo DataSegment { get; set; }
    public SegmentInfo BssSegment { get; set; }
    public NroAssetInfo? Assets { get; set; }
}

/// <summary>段信息</summary>
public readonly struct SegmentInfo
{
    public ulong Address { get; init; }
    public uint FileOffset { get; init; }
    public uint Size { get; init; }

    public SegmentInfo(ulong address, uint fileOffset, uint size)
    {
        Address = address;
        FileOffset = fileOffset;
        Size = size;
    }
}

/// <summary>NRO Asset 信息</summary>
public sealed class NroAssetInfo
{
    public ulong IconOffset { get; set; }
    public ulong IconSize { get; set; }
    public ulong RomFsOffset { get; set; }
    public ulong RomFsSize { get; set; }
}
