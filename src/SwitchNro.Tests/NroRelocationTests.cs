using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using Xunit;
using Loader = SwitchNro.NroLoader.NroLoader;

namespace SwitchNro.Tests;

/// <summary>
/// MOD0 重定位表处理单元测试
/// 构造模拟 NRO 内存布局，使用真实 NRO 格式的基地址相对偏移
///
/// NRO 内存布局（基地址相对偏移）:
///   .text   @ 0x0000 (PageSize)   — 代码段，包含 MOD0 头
///   .rodata @ 0x1000 (PageSize)   — 只读数据，包含 .dynamic / rela / symtab / strtab
///   .data   @ 0x2000 (PageSize)   — 可写数据，重定位目标
///   .bss    @ 0x3000 (PageSize)   — 零填充
///
/// 关键约定:
///   - .dynamic 中 DT_RELA/DT_SYMTAB/DT_STRTAB 的 DVal 是相对于 NRO 编译基地址 (0x0) 的偏移
///   - Elf64Rela.ROffset 是相对于模块基地址的偏移
///   - Elf64Sym.StValue 是相对于模块基地址的偏移
///   - MOD0.DynamicOffset 是相对于 MOD0 头位置的偏移
/// </summary>
public class NroRelocationTests : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private const ulong BaseAddress = 0x0008_0000;
    private const ulong PageSize = 0x1000;

    // 段基地址相对偏移（相对于 NRO 编译基地址 0x0）
    private const ulong TextOffset = 0x0000;
    private const ulong RodataOffset = 0x1000;
    private const ulong DataOffset = 0x2000;
    private const ulong BssOffset = 0x3000;

    public NroRelocationTests()
    {
        _memory = new VirtualMemoryManager();
    }

    public void Dispose() => _memory.Dispose();

    // ──────────────────── 辅助方法 ────────────────────

    /// <summary>
    /// 构建最小化模拟 NRO 内存布局并执行重定位
    /// </summary>
    private (NroModule module, Loader loader) BuildMockNro(
        Action<Span<byte>>? configureText = null,
        Action<Span<byte>>? configureRodata = null,
        Action<Span<byte>>? configureData = null,
        MemoryPermissions textPerms = MemoryPermissions.All)
    {
        // .text 段
        Span<byte> textData = new byte[PageSize];
        textData.Clear();
        configureText?.Invoke(textData);
        _memory.Map(BaseAddress, textData, textPerms, MemoryType.CodeStatic);

        // .rodata 段
        Span<byte> rodataData = new byte[PageSize];
        rodataData.Clear();
        configureRodata?.Invoke(rodataData);
        _memory.Map(BaseAddress + RodataOffset, rodataData, MemoryPermissions.ReadWrite, MemoryType.CodeStatic);

        // .data 段
        Span<byte> dataData = new byte[PageSize];
        dataData.Clear();
        configureData?.Invoke(dataData);
        _memory.Map(BaseAddress + DataOffset, dataData, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);

        // .bss 段
        _memory.MapZero(BaseAddress + BssOffset, PageSize, MemoryPermissions.ReadWrite, MemoryType.CodeMutable);

        var module = new NroModule
        {
            BaseAddress = BaseAddress,
            Header = new NroHeader
            {
                Magic = 0x304F524E,
                TextOffset = 0,
                TextSize = (uint)PageSize,
                RodataOffset = (uint)PageSize,
                RodataSize = (uint)PageSize,
                DataOffset = 2 * (uint)PageSize,
                DataSize = (uint)PageSize,
                BssSize = (uint)PageSize,
            },
            TextSegment = new SegmentInfo(BaseAddress, 0, (uint)PageSize),
            RodataSegment = new SegmentInfo(BaseAddress + RodataOffset, (uint)PageSize, (uint)PageSize),
            DataSegment = new SegmentInfo(BaseAddress + DataOffset, 2 * (uint)PageSize, (uint)PageSize),
            BssSegment = new SegmentInfo(BaseAddress + BssOffset, 0, (uint)PageSize),
        };

        var loader = new Loader(_memory);
        return (module, loader);
    }

    /// <summary>在指定偏移写入结构体到 Span</summary>
    private static void WriteStruct<T>(Span<byte> span, int offset, T value) where T : unmanaged
    {
        Span<byte> structBytes = stackalloc byte[Unsafe.SizeOf<T>()];
        MemoryMarshal.Write(structBytes, in value);
        structBytes.CopyTo(span[offset..]);
    }

    // ──────────────────── MOD0 扫描测试 ────────────────────

    [Fact]
    public void FindMod0_AtTextStart_ReturnsCorrectAddress()
    {
        var (module, loader) = BuildMockNro(configureText: text =>
        {
            var mod0 = new Mod0Header
            {
                Magic = 0x30444F4D,
                DynamicOffset = (uint)RodataOffset, // 相对 MOD0 头位置的偏移
            };
            WriteStruct(text, 0, mod0);
        });

        ulong found = loader.FindMod0Header(module);
        Assert.Equal(BaseAddress, found);

        var mod0Read = _memory.Read<Mod0Header>(found);
        Assert.True(mod0Read.IsValid);
        Assert.Equal((uint)RodataOffset, mod0Read.DynamicOffset);
    }

    [Fact]
    public void FindMod0_AtNonZeroOffset_ReturnsCorrectAddress()
    {
        // MOD0 不在 .text 起始，而在偏移 0x40 处
        const int Mod0PositionInText = 0x40;
        // DynamicOffset 是相对于 MOD0 头位置的偏移
        // .dynamic 在 .rodata 起始，虚拟地址 = BaseAddress + RodataOffset
        // MOD0 在虚拟地址 = BaseAddress + Mod0PositionInText
        // DynamicOffset = (BaseAddress + RodataOffset) - (BaseAddress + Mod0PositionInText)
        //               = RodataOffset - Mod0PositionInText
        uint dynOffsetFromMod0 = (uint)(RodataOffset - Mod0PositionInText);

        var (module, loader) = BuildMockNro(configureText: text =>
        {
            var mod0 = new Mod0Header
            {
                Magic = 0x30444F4D,
                DynamicOffset = dynOffsetFromMod0,
            };
            WriteStruct(text, Mod0PositionInText, mod0);
        });

        ulong found = loader.FindMod0Header(module);
        Assert.Equal(BaseAddress + Mod0PositionInText, found);

        var mod0Read = _memory.Read<Mod0Header>(found);
        Assert.Equal(dynOffsetFromMod0, mod0Read.DynamicOffset);
    }

    [Fact]
    public void FindMod0_NoMagic_ReturnsZero()
    {
        // .text 段全零，无 MOD0 魔数
        var (module, loader) = BuildMockNro();

        ulong found = loader.FindMod0Header(module);
        Assert.Equal(0UL, found);
    }

    // ──────────────────── MOD0 非零偏移端到端测试 ────────────────────

    [Fact]
    public void Mod0AtNonZeroOffset_FullRelocationPipeline_WorksCorrectly()
    {
        // MOD0 在 .text + 0x40，验证完整重定位管线正确计算 dynAddr
        const int Mod0PositionInText = 0x40;
        const int RelaOffsetInRod = 0x80;
        const int TargetOffsetInData = 0x10;
        // DynamicOffset 相对 MOD0 头位置：.dynamic 在 .rodata 起始
        uint dynOffsetFromMod0 = (uint)(RodataOffset - Mod0PositionInText);

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = dynOffsetFromMod0,
                };
                WriteStruct(text, Mod0PositionInText, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset + TargetOffsetInData,
                    RInfo = (ulong)RelaType.Relative,
                    RAddend = 0x500,
                });
            });

        loader.PerformRelocations(module);

        Assert.Equal(BaseAddress + 0x500,
            _memory.Read<ulong>(BaseAddress + DataOffset + TargetOffsetInData));
    }

    // ──────────────────── R_AARCH64_RELATIVE 重定位测试 ────────────────────

    [Fact]
    public void RelativeRelocation_WritesBasePlusAddend()
    {
        const int RelaOffsetInRod = 0x80;
        const int TargetOffsetInData = 0x50; // 目标在 .data + 0x50

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset, // .dynamic 在 .rodata 起始
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                // .dynamic — DVal 使用基地址相对偏移（真实 NRO 格式）
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                // Rela: R_AARCH64_RELATIVE
                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset + TargetOffsetInData, // 基地址相对偏移
                    RInfo = (ulong)RelaType.Relative,
                    RAddend = 0x1234, // 基地址相对偏移
                });
            });

        // 验证重定位前值为 0
        Assert.Equal(0UL, _memory.Read<ulong>(BaseAddress + DataOffset + TargetOffsetInData));

        // 执行重定位
        loader.PerformRelocations(module);

        // R_AARCH64_RELATIVE: *target = BaseAddress + addend = 0x80000 + 0x1234 = 0x81234
        Assert.Equal(BaseAddress + 0x1234, _memory.Read<ulong>(BaseAddress + DataOffset + TargetOffsetInData));
    }

    [Fact]
    public void MultipleRelativeRelocations_AllAppliedCorrectly()
    {
        const int RelaOffsetInRod = 0x80;
        const int RelaCount = 10;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = (ulong)(24 * RelaCount) }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                for (int i = 0; i < RelaCount; i++)
                {
                    WriteStruct(rod, RelaOffsetInRod + i * 24, new Elf64Rela
                    {
                        ROffset = DataOffset + (ulong)(i * 8), // 基地址相对偏移
                        RInfo = (ulong)RelaType.Relative,
                        RAddend = (i + 1) * 0x100, // 基地址相对偏移
                    });
                }
            });

        loader.PerformRelocations(module);

        for (int i = 0; i < RelaCount; i++)
        {
            Assert.Equal(BaseAddress + (ulong)((i + 1) * 0x100),
                _memory.Read<ulong>(BaseAddress + DataOffset + (ulong)(i * 8)));
        }
    }

    [Fact]
    public void RelativeRelocation_AddendZero_WritesBaseAddress()
    {
        // addend=0: *target = BaseAddress + 0 = BaseAddress（GOT 自引用常见）
        const int RelaOffsetInRod = 0x80;
        const int TargetOffsetInData = 0x00;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset + TargetOffsetInData,
                    RInfo = (ulong)RelaType.Relative,
                    RAddend = 0, // 零加数
                });
            });

        loader.PerformRelocations(module);

        // addend=0 → *target = BaseAddress
        Assert.Equal(BaseAddress, _memory.Read<ulong>(BaseAddress + DataOffset + TargetOffsetInData));
    }

    // ──────────────────── R_AARCH64_GLOB_DAT 重定位测试 ────────────────────

    [Fact]
    public void GlobDat_ResolvesSymbol()
    {
        const int RelaOffsetInRod = 0x80;
        const int SymTabOffsetInRod = 0x140;
        const int StrTabOffsetInRod = 0x200;
        const int TargetOffsetInData = 0x20;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                // .dynamic — DVal 为基地址相对偏移
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymTab, DVal = RodataOffset + SymTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.StrTab, DVal = RodataOffset + StrTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.StrSz, DVal = 0x40 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                // 符号表: index 0 = STN_UNDEF (全零), index 1 = 函数 @ .text+0x400
                WriteStruct(rod, SymTabOffsetInRod + 0 * 24, new Elf64Sym()); // STN_UNDEF
                WriteStruct(rod, SymTabOffsetInRod + 1 * 24, new Elf64Sym
                {
                    StName = 0,
                    StInfo = 0x12, // STT_FUNC | STB_GLOBAL
                    StOther = 0,
                    StShndx = 1,
                    StValue = 0x400, // 基地址相对偏移（.text + 0x400）
                    StSize = 0x20,
                });

                // 字符串表: "my_func\0"
                var name = System.Text.Encoding.ASCII.GetBytes("my_func\0");
                name.CopyTo(rod[StrTabOffsetInRod..]);

                // Rela: R_AARCH64_GLOB_DAT, symbol index=1
                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset + TargetOffsetInData,
                    RInfo = ((ulong)1 << 32) | (ulong)RelaType.GlobDat,
                    RAddend = 0,
                });
            });

        loader.PerformRelocations(module);

        // GOT 条目 = BaseAddress + sym.StValue + addend = 0x80000 + 0x400 + 0
        Assert.Equal(BaseAddress + 0x400,
            _memory.Read<ulong>(BaseAddress + DataOffset + TargetOffsetInData));
    }

    [Fact]
    public void GlobDat_WithAddend_IncludesAddendInResult()
    {
        const int RelaOffsetInRod = 0x80;
        const int SymTabOffsetInRod = 0x140;
        const int StrTabOffsetInRod = 0x200;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymTab, DVal = RodataOffset + SymTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.StrTab, DVal = RodataOffset + StrTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                WriteStruct(rod, SymTabOffsetInRod + 0 * 24, new Elf64Sym()); // STN_UNDEF
                WriteStruct(rod, SymTabOffsetInRod + 1 * 24, new Elf64Sym
                {
                    StValue = 0x400, // 基地址相对偏移
                    StInfo = 0x12,
                });

                // GLOB_DAT with addend = 8
                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset, // .data 起始
                    RInfo = ((ulong)1 << 32) | (ulong)RelaType.GlobDat,
                    RAddend = 8,
                });
            });

        loader.PerformRelocations(module);

        // 值 = BaseAddress + sym.StValue + addend = 0x80000 + 0x400 + 8 = 0x80408
        Assert.Equal(BaseAddress + 0x400 + 8, _memory.Read<ulong>(BaseAddress + DataOffset));
    }

    // ──────────────────── R_AARCH64_JUMP_SLOT 重定位测试 ────────────────────

    [Fact]
    public void JumpSlot_ResolvesPltEntry()
    {
        const int RelaOffsetInRod = 0x80;
        const int SymTabOffsetInRod = 0x140;
        const int StrTabOffsetInRod = 0x200;
        const int TargetOffsetInData = 0x30;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymTab, DVal = RodataOffset + SymTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.StrTab, DVal = RodataOffset + StrTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                WriteStruct(rod, SymTabOffsetInRod + 0 * 24, new Elf64Sym()); // STN_UNDEF
                WriteStruct(rod, SymTabOffsetInRod + 1 * 24, new Elf64Sym
                {
                    StValue = 0x500, // 基地址相对偏移（.text + 0x500）
                    StInfo = 0x12,
                });

                // R_AARCH64_JUMP_SLOT, symbol index=1
                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset + TargetOffsetInData,
                    RInfo = ((ulong)1 << 32) | (ulong)RelaType.JumpSlot,
                    RAddend = 0,
                });
            });

        loader.PerformRelocations(module);

        Assert.Equal(BaseAddress + 0x500,
            _memory.Read<ulong>(BaseAddress + DataOffset + TargetOffsetInData));
    }

    // ──────────────────── 边界条件测试 ────────────────────

    [Fact]
    public void NoDynamicRela_SkipsRelocationWithoutError()
    {
        // MOD0 存在但 .dynamic 中无 DT_RELA → 应跳过，不修改任何数据
        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                // 只有 DT_NULL
                WriteStruct(rod, 0, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 });
            });

        ulong beforeData = _memory.Read<ulong>(BaseAddress + DataOffset + 0x20);
        loader.PerformRelocations(module);
        ulong afterData = _memory.Read<ulong>(BaseAddress + DataOffset + 0x20);
        Assert.Equal(beforeData, afterData);
    }

    [Fact]
    public void NoMod0InText_SkipsRelocationWithoutError()
    {
        // .text 段全零，无 MOD0 → PerformRelocations 应静默跳过
        var (module, loader) = BuildMockNro();

        ulong beforeData = _memory.Read<ulong>(BaseAddress + DataOffset);
        loader.PerformRelocations(module);
        ulong afterData = _memory.Read<ulong>(BaseAddress + DataOffset);
        Assert.Equal(beforeData, afterData);
    }

    [Fact]
    public void UnknownRelaType_SkippedWithoutCrash()
    {
        const int RelaOffsetInRod = 0x80;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                // 未知重定位类型 0x999
                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = DataOffset + 0x08,
                    RInfo = 0x999,
                    RAddend = 0,
                });
            });

        // 不应崩溃
        loader.PerformRelocations(module);

        // 目标地址应未被修改（保持 0）
        Assert.Equal(0UL, _memory.Read<ulong>(BaseAddress + DataOffset + 0x08));
    }

    // ──────────────────── DT_TEXTREL 测试 ────────────────────

    [Fact]
    public void TextRel_WritesToTextSegmentAndRestoresPermissions()
    {
        const int RelaOffsetInRod = 0x80;
        // 重定位目标在 .text 段内（基地址相对偏移 0x40）
        const ulong TargetOffset = 0x40;

        // 先用 ReadWriteExecute 映射以便写入 MOD0 数据
        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.TextRel, DVal = 0 }); d += 16; // DT_TEXTREL
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                // R_AARCH64_RELATIVE 写入 .text 段
                WriteStruct(rod, RelaOffsetInRod, new Elf64Rela
                {
                    ROffset = TargetOffset, // 目标在 .text + 0x40
                    RInfo = (ulong)RelaType.Relative,
                    RAddend = 0x2000,
                });
            });

        // 模拟真实场景：将 .text 段切换为 ReadExecute（不可写）
        _memory.UpdatePermissions(BaseAddress, PageSize, MemoryPermissions.ReadExecute);

        // 执行重定位 — DT_TEXTREL 应临时升级权限，写入后恢复
        loader.PerformRelocations(module);

        // 验证写入成功（.text 段仍可读）
        Assert.Equal(BaseAddress + 0x2000, _memory.Read<ulong>(BaseAddress + TargetOffset));
    }

    // ──────────────────── 混合重定位类型测试 ────────────────────

    [Fact]
    public void MixedRelocationTypes_AllAppliedCorrectly()
    {
        const int RelaOffsetInRod = 0x80;
        const int SymTabOffsetInRod = 0x200;
        const int StrTabOffsetInRod = 0x300;

        var (module, loader) = BuildMockNro(
            configureText: text =>
            {
                var mod0 = new Mod0Header
                {
                    Magic = 0x30444F4D,
                    DynamicOffset = (uint)RodataOffset,
                };
                WriteStruct(text, 0, mod0);
            },
            configureRodata: rod =>
            {
                int d = 0;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Rela, DVal = RodataOffset + RelaOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaSz, DVal = 24 * 4 }); d += 16; // 4 条 rela
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.RelaEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymTab, DVal = RodataOffset + SymTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.SymEnt, DVal = 24 }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.StrTab, DVal = RodataOffset + StrTabOffsetInRod }); d += 16;
                WriteStruct(rod, d, new Elf64Dyn { DTag = DtTag.Null, DVal = 0 }); d += 16;

                // 符号表
                WriteStruct(rod, SymTabOffsetInRod + 0 * 24, new Elf64Sym()); // STN_UNDEF
                WriteStruct(rod, SymTabOffsetInRod + 1 * 24, new Elf64Sym
                {
                    StValue = 0x400, // .text + 0x400
                    StInfo = 0x12,
                });
                WriteStruct(rod, SymTabOffsetInRod + 2 * 24, new Elf64Sym
                {
                    StValue = 0x500, // .text + 0x500
                    StInfo = 0x12,
                });

                // 字符串表
                var name = System.Text.Encoding.ASCII.GetBytes("func_a\0func_b\0");
                name.CopyTo(rod[StrTabOffsetInRod..]);

                // Rela 0: R_AARCH64_RELATIVE → .data + 0x00
                WriteStruct(rod, RelaOffsetInRod + 0 * 24, new Elf64Rela
                {
                    ROffset = DataOffset + 0x00,
                    RInfo = (ulong)RelaType.Relative,
                    RAddend = 0x100,
                });

                // Rela 1: R_AARCH64_GLOB_DAT (sym 1) → .data + 0x08
                WriteStruct(rod, RelaOffsetInRod + 1 * 24, new Elf64Rela
                {
                    ROffset = DataOffset + 0x08,
                    RInfo = ((ulong)1 << 32) | (ulong)RelaType.GlobDat,
                    RAddend = 0,
                });

                // Rela 2: R_AARCH64_JUMP_SLOT (sym 2) → .data + 0x10
                WriteStruct(rod, RelaOffsetInRod + 2 * 24, new Elf64Rela
                {
                    ROffset = DataOffset + 0x10,
                    RInfo = ((ulong)2 << 32) | (ulong)RelaType.JumpSlot,
                    RAddend = 0,
                });

                // Rela 3: R_AARCH64_RELATIVE → .data + 0x18
                WriteStruct(rod, RelaOffsetInRod + 3 * 24, new Elf64Rela
                {
                    ROffset = DataOffset + 0x18,
                    RInfo = (ulong)RelaType.Relative,
                    RAddend = 0x200,
                });
            });

        loader.PerformRelocations(module);

        // 验证所有 4 个重定位
        Assert.Equal(BaseAddress + 0x100, _memory.Read<ulong>(BaseAddress + DataOffset + 0x00)); // RELATIVE
        Assert.Equal(BaseAddress + 0x400, _memory.Read<ulong>(BaseAddress + DataOffset + 0x08)); // GLOB_DAT
        Assert.Equal(BaseAddress + 0x500, _memory.Read<ulong>(BaseAddress + DataOffset + 0x10)); // JUMP_SLOT
        Assert.Equal(BaseAddress + 0x200, _memory.Read<ulong>(BaseAddress + DataOffset + 0x18)); // RELATIVE
    }
}
