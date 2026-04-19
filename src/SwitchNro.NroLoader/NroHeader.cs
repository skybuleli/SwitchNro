using System.Runtime.InteropServices;

namespace SwitchNro.NroLoader;

// ──────────────────── ELF 动态链接常量 ────────────────────

/// <summary>ELF64 动态段标签 (d_tag)</summary>
public static class DtTag
{
    public const long Null = 0;       // DT_NULL — 段结束标记
    public const long StrTab = 5;     // DT_STRTAB — 字符串表地址
    public const long SymTab = 6;     // DT_SYMTAB — 符号表地址
    public const long Rela = 7;       // DT_RELA — 重定位表地址
    public const long RelaSz = 8;     // DT_RELASZ — 重定位表大小
    public const long RelaEnt = 9;    // DT_RELAENT — 重定位条目大小
    public const long StrSz = 10;     // DT_STRSZ — 字符串表大小
    public const long SymEnt = 11;    // DT_SYMENT — 符号条目大小
    public const long TextRel = 14;   // DT_TEXTREL — 重定位可能修改只读段
}

/// <summary>AArch64 ELF 重定位类型</summary>
public static class RelaType
{
    /// <summary>R_AARCH64_GLOB_DAT — 全局数据 GOT 条目</summary>
    public const int GlobDat = 0x401;
    /// <summary>R_AARCH64_JUMP_SLOT — PLT 跳转槽</summary>
    public const int JumpSlot = 0x402;
    /// <summary>R_AARCH64_RELATIVE — 基地址相对重定位</summary>
    public const int Relative = 0x403;
}

/// <summary>
/// ELF64 动态段条目 (16 字节)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Elf64Dyn
{
    /// <summary>条目类型 (DtTag 值)</summary>
    public long DTag;
    /// <summary>值 (地址或整数，取决于 DTag)</summary>
    public ulong DVal;
}

/// <summary>
/// ELF64 重定位条目 (24 字节)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Elf64Rela
{
    /// <summary>需要修改的位置偏移（相对于模块基地址）</summary>
    public ulong ROffset;
    /// <summary>符号索引 + 重定位类型 (高32位=符号索引, 低32位=类型)</summary>
    public ulong RInfo;
    /// <summary>加数</summary>
    public long RAddend;

    /// <summary>重定位类型</summary>
    public readonly int Type => (int)(RInfo & 0xFFFFFFFFUL);
    /// <summary>符号表索引</summary>
    public readonly int SymbolIndex => (int)(RInfo >> 32);
}

/// <summary>
/// ELF64 符号表条目 (24 字节)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Elf64Sym
{
    /// <summary>符号名在字符串表中的偏移</summary>
    public uint StName;
    /// <summary>符号类型和绑定属性</summary>
    public byte StInfo;
    /// <summary>可见性</summary>
    public byte StOther;
    /// <summary>所属段索引</summary>
    public ushort StShndx;
    /// <summary>符号值（地址偏移，相对于模块基地址）</summary>
    public ulong StValue;
    /// <summary>符号大小</summary>
    public ulong StSize;
}

/// <summary>
/// NRO 文件头 (0x70 字节)
/// 与 Switch homebrew NRO0 格式完全对齐
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NroHeader
{
    /// <summary>魔数 "NRO0"</summary>
    public uint Magic;

    /// <summary>版本号 (通常为 0)</summary>
    public uint Version;

    /// <summary>整个 NRO 文件的大小</summary>
    public uint Size;

    /// <summary>未使用</summary>
    public uint Reserved0;

    // .text 段 (代码段，可执行)
    /// <summary>.text 段在文件中的偏移</summary>
    public uint TextOffset;
    /// <summary>.text 段大小</summary>
    public uint TextSize;

    // .rodata 段 (只读数据)
    /// <summary>.rodata 段在文件中的偏移</summary>
    public uint RodataOffset;
    /// <summary>.rodata 段大小</summary>
    public uint RodataSize;

    // .data 段 (可写数据)
    /// <summary>.data 段在文件中的偏移</summary>
    public uint DataOffset;
    /// <summary>.data 段大小</summary>
    public uint DataSize;

    // .bss 段 (零初始化数据，不在文件中)
    /// <summary>.bss 段大小</summary>
    public uint BssSize;

    /// <summary>未使用</summary>
    public uint Reserved1;

    // 模块名 (MOD0 扩展)
    /// <summary>模块名偏移</summary>
    public uint ModuleNameOffset;

    // 各段页对齐大小
    /// <summary>.text 段页对齐大小</summary>
    public uint TextPageSize;
    /// <summary>.rodata 段页对齐大小</summary>
    public uint RodataPageSize;
    /// <summary>.data 段页对齐大小</summary>
    public uint DataPageSize;
    /// <summary>.bss 段页对齐大小</summary>
    public uint BssPageSize;

    /// <summary>构建 ID (SHA256 前 8 字节)</summary>
    public ulong BuildId;

    /// <summary>.text 段压缩标志</summary>
    public uint TextCompress;
    /// <summary>.rodata 段压缩标志</summary>
    public uint RodataCompress;
    /// <summary>.data 段压缩标志</summary>
    public uint DataCompress;

    /// <summary>依赖模块数</summary>
    public uint DependencyCount;

    /// <summary>依赖模块偏移</summary>
    public uint DependencyOffset;

    /// <summary>保留字段</summary>
    public uint Reserved2;
    public uint Reserved3;
    public uint Reserved4;
    public uint Reserved5;

    /// <summary>验证魔数是否正确</summary>
    public readonly bool IsValid => Magic == 0x304F524E; // "NRO0" little-endian
}

/// <summary>
/// NRO Asset Section 头 (可选，追加在 NRO 主体之后)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AssetHeader
{
    /// <summary>魔数 "ASET"</summary>
    public uint Magic;

    /// <summary>Asset Header 版本</summary>
    public uint Version;

    /// <summary>图标偏移</summary>
    public ulong IconOffset;
    /// <summary>图标大小</summary>
    public ulong IconSize;

    /// <summary>截图偏移</summary>
    public ulong ScreenshotOffset;
    /// <summary>截图大小</summary>
    public ulong ScreenshotSize;

    /// <summary>RomFS 偏移</summary>
    public ulong RomFsOffset;
    /// <summary>RomFS 大小</summary>
    public ulong RomFsSize;

    /// <summary>验证魔数是否正确</summary>
    public readonly bool IsValid => Magic == 0x54455341; // "ASET" little-endian
}

/// <summary>
/// MOD0 段头 (模块元数据，NRO 内嵌)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Mod0Header
{
    /// <summary>魔数 "MOD0"</summary>
    public uint Magic;
    /// <summary>动态重定位表相对偏移</summary>
    public uint DynamicOffset;
    /// <summary>BSS 起始相对偏移</summary>
    public uint BssStartOffset;
    /// <summary>BSS 结束相对偏移</summary>
    public uint BssEndOffset;
    /// <summary>异常处理相关偏移</summary>
    public uint EhFrameHdrOffset;
    /// <summary>未使用</summary>
    public uint Reserved;

    /// <summary>验证魔数是否正确</summary>
    public readonly bool IsValid => Magic == 0x30444F4D; // "MOD0" little-endian
}
