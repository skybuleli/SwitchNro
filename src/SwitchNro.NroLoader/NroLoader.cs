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

    /// <summary>Homebrew NRO 头部在文件中的偏移（前 16 字节为 preamble 分支指令）</summary>
    private const int HomebrewHeaderOffset = 0x10;

    /// <summary>加载 NRO 文件并返回加载信息</summary>
    public NroModule Load(string filePath)
    {
        Logger.Info(nameof(NroLoader), $"开始加载 NRO: {filePath}");

        using var stream = File.OpenRead(filePath);

        // 检测 NRO 头部偏移：
        //   标准 homebrew NRO: 头部在偏移 0x10（前 16 字节为 preamble 分支指令）
        //   测试/自定义 NRO:   头部在偏移 0x00
        int headerOffset = DetectHeaderOffset(stream);

        var header = SpanHelper.ReadStruct<NroHeader>(stream, headerOffset);

        if (!header.IsValid)
            throw new InvalidDataException($"无效的 NRO 文件: 魔数不匹配 0x{header.Magic:X8}");

        Logger.Info(nameof(NroLoader), $"NRO 头部偏移: 0x{headerOffset:X} (homebrew={headerOffset != 0})");

        // ASLR: 在有效范围内随机化基地址 (对齐 4KB)
        var random = new Random();
        ulong aslrSlot = (ulong)(random.Next() & 0x1FFFF) << 12; // 25-bit 随机，对齐 4KB
        ulong basePath = 0x0008_0000 + aslrSlot; // 从 512KB 开始，避免零页

        var module = LoadFromStream(stream, header, basePath);

        // 检查 Asset Section（header.Size 是从文件起始计算的 NRO 主体大小）
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

    /// <summary>
    /// 检测 NRO 头部在文件中的偏移
    /// 标准 homebrew NRO 文件: 前 16 字节为 preamble (ARM64 分支指令)，
    ///   NRO0 魔数在偏移 0x10
    /// 测试/自定义 NRO: NRO0 魔数在偏移 0x00
    /// </summary>
    private static int DetectHeaderOffset(Stream stream)
    {
        // 先检查偏移 0x10 是否有 NRO0 魔数
        if (stream.Length >= 0x10 + 4)
        {
            var magicAt10 = SpanHelper.ReadStruct<uint>(stream, 0x10);
            if (magicAt10 == 0x304F524E) // "NRO0" little-endian
                return 0x10;
        }

        // 再检查偏移 0x00 是否有 NRO0 魔数
        if (stream.Length >= 4)
        {
            var magicAt0 = SpanHelper.ReadStruct<uint>(stream, 0);
            if (magicAt0 == 0x304F524E)
                return 0;
        }

        // 默认尝试偏移 0（兼容旧逻辑）
        return 0;
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

        // 映射 .text 段 (代码，可读可执行) — MemoryType.CodeStatic
        Span<byte> textData = new byte[header.TextSize];
        stream.Position = header.TextOffset;
        stream.ReadExactly(textData);
        _memory.Map(textAddr, textData, MemoryPermissions.ReadExecute, MemoryType.CodeStatic);

        // 映射 .rodata 段 (只读数据) — MemoryType.CodeStatic（与 .text 同属代码静态区域）
        Span<byte> rodataData = new byte[header.RodataSize];
        stream.Position = header.RodataOffset;
        stream.ReadExactly(rodataData);
        _memory.Map(rodataAddr, rodataData, MemoryPermissions.Read, MemoryType.CodeStatic);

        // 映射 .data 段 (可写数据) — MemoryType.CodeMutable（可写数据段）
        Span<byte> dataData = new byte[header.DataSize];
        stream.Position = header.DataOffset;
        stream.ReadExactly(dataData);
        _memory.Map(dataAddr, dataData, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);

        // 映射 .bss 段 (零填充) — MemoryType.CodeMutable（与 .data 同属可写数据区域）
        if (header.BssSize > 0)
        {
            _memory.MapZero(bssAddr, header.BssSize, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);
        }

        // 执行重定位
        PerformRelocations(module);

        // 设置入口点
        module.EntryPoint = textAddr; // NRO 入口点默认为 .text 段起始

        return module;
    }

    /// <summary>执行 NRO 重定位</summary>
    internal void PerformRelocations(NroModule module)
    {
        // 1. 扫描 .text 段寻找 MOD0 魔数
        ulong mod0Addr = FindMod0Header(module);
        if (mod0Addr == 0)
        {
            Logger.Debug(nameof(NroLoader), "未找到 MOD0 头，跳过重定位");
            return;
        }

        var mod0 = _memory.Read<Mod0Header>(mod0Addr);
        Logger.Info(nameof(NroLoader),
            $"找到 MOD0 @ 0x{mod0Addr:X16}, DynamicOffset=0x{mod0.DynamicOffset:X}");

        // 2. 解析 .dynamic 段
        //    MOD0.DynamicOffset 是相对于 MOD0 头位置的偏移
        ulong dynAddr = mod0Addr + mod0.DynamicOffset;
        var dynInfo = ParseDynamicSection(dynAddr);

        if (dynInfo.RelaOffset == 0 || dynInfo.RelaSize == 0)
        {
            Logger.Debug(nameof(NroLoader), ".dynamic 段无 DT_RELA 条目，跳过重定位");
            return;
        }

        // .dynamic 中的地址值是相对于 NRO 编译基地址 (0x0) 的偏移
        // 加载到虚拟内存后需要加上 ASLR 基地址
        dynInfo.RelaAddress = module.BaseAddress + dynInfo.RelaOffset;
        dynInfo.SymTabAddress = module.BaseAddress + dynInfo.SymTabOffset;
        dynInfo.StrTabAddress = module.BaseAddress + dynInfo.StrTabOffset;

        Logger.Info(nameof(NroLoader),
            $"动态段: Rela=0x{dynInfo.RelaAddress:X} Size={dynInfo.RelaSize} " +
            $"SymTab=0x{dynInfo.SymTabAddress:X} StrTab=0x{dynInfo.StrTabAddress:X} " +
            $"HasTextRel={dynInfo.HasTextRel}");

        // 3. 如果有 DT_TEXTREL，临时赋予 .text 段写权限
        bool textRelApplied = false;
        if (dynInfo.HasTextRel)
        {
            _memory.UpdatePermissions(module.TextSegment.Address,
                AlignPage(module.Header.TextSize), MemoryPermissions.All);
            textRelApplied = true;
            Logger.Debug(nameof(NroLoader), "DT_TEXTREL: 临时赋予 .text 段写权限");
        }

        try
        {
            // 4. 处理重定位条目
            ApplyRelocations(module, dynInfo);
        }
        finally
        {
            // 5. 恢复 .text 段权限
            if (textRelApplied)
            {
                _memory.UpdatePermissions(module.TextSegment.Address,
                    AlignPage(module.Header.TextSize), MemoryPermissions.ReadExecute);
                Logger.Debug(nameof(NroLoader), "DT_TEXTREL: 恢复 .text 段只读权限");
            }
        }
    }

    /// <summary>扫描 .text 段寻找 MOD0 魔数</summary>
    internal ulong FindMod0Header(NroModule module)
    {
        const uint Mod0Magic = 0x30444F4D; // "MOD0" little-endian
        ulong textStart = module.BaseAddress;
        ulong textEnd = textStart + module.TextSegment.Size;

        // 以 4 字节步长扫描（MOD0 头 4 字节对齐）
        for (ulong addr = textStart; addr + (ulong)Unsafe.SizeOf<Mod0Header>() <= textEnd; addr += 4)
        {
            if (_memory.Read<uint>(addr) == Mod0Magic)
            {
                return addr;
            }
        }

        return 0;
    }

    /// <summary>解析 .dynamic 段，提取重定位所需信息</summary>
    /// <remarks>
    /// .dynamic 中的地址值 (DT_RELA, DT_SYMTAB, DT_STRTAB) 是相对于
    /// NRO 编译基地址 (0x0) 的偏移，尚未加上 ASLR 基地址。
    /// 调用方需在验证后将偏移转为绝对虚拟地址。
    /// </remarks>
    internal DynamicSectionInfo ParseDynamicSection(ulong dynAddr)
    {
        var info = new DynamicSectionInfo();

        // 遍历 Elf64Dyn 条目直到 DT_NULL
        for (int i = 0; i < 256; i++) // 安全上限，防止无限循环
        {
            var entry = _memory.Read<Elf64Dyn>(dynAddr + (ulong)(i * Unsafe.SizeOf<Elf64Dyn>()));

            if (entry.DTag == DtTag.Null)
                break;

            switch (entry.DTag)
            {
                case DtTag.StrTab:
                    info.StrTabOffset = entry.DVal;
                    break;
                case DtTag.SymTab:
                    info.SymTabOffset = entry.DVal;
                    break;
                case DtTag.Rela:
                    info.RelaOffset = entry.DVal;
                    break;
                case DtTag.RelaSz:
                    info.RelaSize = entry.DVal;
                    break;
                case DtTag.RelaEnt:
                    info.RelaEntSize = entry.DVal;
                    break;
                case DtTag.StrSz:
                    info.StrTabSize = entry.DVal;
                    break;
                case DtTag.SymEnt:
                    info.SymEntSize = entry.DVal;
                    break;
                case DtTag.TextRel:
                    info.HasTextRel = true;
                    break;
            }
        }

        // DT_RELAENT 默认值 = 24 (Elf64Rela 大小)
        if (info.RelaEntSize == 0)
            info.RelaEntSize = 24;

        // DT_SYMENT 默认值 = 24 (Elf64Sym 大小)
        if (info.SymEntSize == 0)
            info.SymEntSize = 24;

        return info;
    }

    /// <summary>应用所有重定位条目</summary>
    internal void ApplyRelocations(NroModule module, DynamicSectionInfo dynInfo)
    {
        int relaCount = (int)(dynInfo.RelaSize / dynInfo.RelaEntSize);
        ulong relaBaseAddr = dynInfo.RelaAddress; // 虚拟地址

        int relativeCount = 0;
        int globDatCount = 0;
        int jumpSlotCount = 0;
        int skippedCount = 0;

        for (int i = 0; i < relaCount; i++)
        {
            var rela = _memory.Read<Elf64Rela>(relaBaseAddr + (ulong)(i * (int)dynInfo.RelaEntSize));
            ulong targetAddr = module.BaseAddress + rela.ROffset;

            switch (rela.Type)
            {
                case RelaType.Relative:
                {
                    // R_AARCH64_RELATIVE: *target = base + addend
                    ulong value = module.BaseAddress + (ulong)rela.RAddend;
                    _memory.Write<ulong>(targetAddr, value);
                    relativeCount++;
                    break;
                }

                case RelaType.GlobDat:
                case RelaType.JumpSlot:
                {
                    // R_AARCH64_GLOB_DAT / JUMP_SLOT: 解析符号
                    if (dynInfo.SymTabAddress == 0)
                    {
                        Logger.Warning(nameof(NroLoader),
                            $"重定位 {i}: 需要符号解析但无 DT_SYMTAB，跳过");
                        skippedCount++;
                        break;
                    }

                    int symIndex = rela.SymbolIndex;
                    ulong symAddr = dynInfo.SymTabAddress + (ulong)(symIndex * (int)dynInfo.SymEntSize);
                    var sym = _memory.Read<Elf64Sym>(symAddr);

                    // 符号值 + 基地址 + 加数
                    ulong symValue = module.BaseAddress + sym.StValue + (ulong)rela.RAddend;
                    _memory.Write<ulong>(targetAddr, symValue);

                    if (rela.Type == RelaType.GlobDat)
                        globDatCount++;
                    else
                        jumpSlotCount++;
                    break;
                }

                default:
                {
                    // 未知重定位类型 — 记录但不中断
                    if (skippedCount < 5) // 限制日志数量
                    {
                        Logger.Warning(nameof(NroLoader),
                            $"重定位 {i}: 未知类型 0x{rela.Type:X} @ offset 0x{rela.ROffset:X}");
                    }
                    skippedCount++;
                    break;
                }
            }
        }

        Logger.Info(nameof(NroLoader),
            $"重定位完成: 共 {relaCount} 条 — " +
            $"RELATIVE={relativeCount}, GLOB_DAT={globDatCount}, " +
            $"JUMP_SLOT={jumpSlotCount}, 跳过={skippedCount}");
    }

    /// <summary>.dynamic 段解析结果</summary>
    /// <remarks>
    /// *Offset 字段存储 .dynamic 中的原始值（相对于 NRO 编译基地址 0x0 的偏移），
    /// *Address 字段由调用方加上 ASLR 基地址后填充，供 ApplyRelocations 使用。
    /// </remarks>
    internal struct DynamicSectionInfo
    {
        // 原始偏移（从 .dynamic 读取，未加基地址）
        public ulong StrTabOffset;
        public ulong SymTabOffset;
        public ulong RelaOffset;

        // 绝对虚拟地址（调用方加上 BaseAddress 后填充）
        public ulong StrTabAddress;
        public ulong SymTabAddress;
        public ulong RelaAddress;

        // 大小/标志
        public ulong StrTabSize;
        public ulong SymEntSize;
        public ulong RelaSize;
        public ulong RelaEntSize;
        public bool HasTextRel;
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

    private static ulong AlignPage(uint size) => (size + 0xFFFul) & ~0xFFFul; // 4KB 对齐 (Horizon OS 标准)
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
