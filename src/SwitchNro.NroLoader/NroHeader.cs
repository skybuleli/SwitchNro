using System.Runtime.InteropServices;

namespace SwitchNro.NroLoader;

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
