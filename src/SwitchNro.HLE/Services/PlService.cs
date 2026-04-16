using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享字体类型枚举
/// 对应 Switch 系统的 SharedFontType
/// </summary>
public enum SharedFontType : uint
{
    /// <summary>标准字体 (Nintendo Standard)</summary>
    Standard = 0,
    /// <summary>简体中文 (Chinese Simplified)</summary>
    ChineseSimplified = 1,
    /// <summary>扩展简体中文 (Extended Chinese Simplified)</summary>
    ExtendedChineseSimplified = 2,
    /// <summary>繁体中文 (Chinese Traditional)</summary>
    ChineseTraditional = 3,
    /// <summary>韩文 (Korean)</summary>
    Korean = 4,
    /// <summary>Nintendo 扩展字体 (Nintendo Extended)</summary>
    NintendoExt = 5,
    /// <summary>字体类型总数</summary>
    Count = 6,
}

/// <summary>
/// 共享字体加载状态
/// </summary>
public enum SharedFontLoadState : uint
{
    /// <summary>未加载</summary>
    NotLoaded = 0,
    /// <summary>已加载</summary>
    Loaded = 1,
}

/// <summary>
/// 单个共享字体的信息
/// </summary>
public sealed class SharedFontInfo
{
    /// <summary>字体类型</summary>
    public SharedFontType Type { get; init; }

    /// <summary>字体数据大小 (字节)</summary>
    public uint Size { get; init; }

    /// <summary>在共享内存中的偏移量</summary>
    public ulong AddressOffset { get; init; }

    /// <summary>是否已加载</summary>
    public bool IsLoaded { get; set; }
}

/// <summary>
/// 共享的 PL 服务状态
/// 管理共享字体的加载状态和元信息
/// </summary>
public sealed class PlState
{
    /// <summary>共享内存区域基地址</summary>
    private const ulong SharedMemoryBase = 0x1000_0000;

    /// <summary>每个字体在共享内存中的间距</summary>
    private const ulong FontMemoryStride = 0x200_000; // 2MB per font slot

    /// <summary>共享字体信息表</summary>
    private readonly Dictionary<SharedFontType, SharedFontInfo> _fontInfos;

    /// <summary>共享内存句柄</summary>
    private readonly int _sharedMemoryHandle = unchecked((int)0xFFFF2000);

    /// <summary>获取共享内存句柄</summary>
    public int SharedMemoryHandle => _sharedMemoryHandle;

    /// <summary>获取共享字体信息表</summary>
    public IReadOnlyDictionary<SharedFontType, SharedFontInfo> FontInfos => _fontInfos;

    public PlState()
    {
        _fontInfos = new Dictionary<SharedFontType, SharedFontInfo>();

        // 初始化每种字体的信息
        // 真实 Switch 中这些字体从系统 NAND 加载，这里提供合理的 stub 值
        var fontTypes = (SharedFontType[])Enum.GetValues<SharedFontType>();
        for (int i = 0; i < fontTypes.Length - 1; i++) // 排除 Count
        {
            var type = fontTypes[i];
            _fontInfos[type] = new SharedFontInfo
            {
                Type = type,
                Size = 0x100000, // stub: 每种字体约 1MB
                AddressOffset = SharedMemoryBase + (ulong)i * FontMemoryStride,
                IsLoaded = true, // stub: 默认标记为已加载
            };
        }
    }

    /// <summary>获取指定类型字体的信息</summary>
    public SharedFontInfo? GetFontInfo(SharedFontType type)
    {
        return _fontInfos.GetValueOrDefault(type);
    }

    /// <summary>请求加载指定类型的字体</summary>
    /// <remarks>在模拟器中字体数据已预加载，此操作为空操作</remarks>
    public void RequestLoad(SharedFontType type)
    {
        if (_fontInfos.TryGetValue(type, out var info))
        {
            info.IsLoaded = true;
            Logger.Debug(nameof(PlState), $"RequestLoad: {type} → marked as loaded");
        }
    }

    /// <summary>获取按优先级排序的字体类型列表</summary>
    /// <param name="language">系统语言 (0=Japanese, 1=English, ...)</param>
    /// <remarks>
    /// 真实 Switch 根据语言决定字体加载优先级
    /// 模拟器中返回所有可用字体的默认顺序
    /// </remarks>
    public static List<SharedFontType> GetFontsInOrderOfPriority(uint language)
    {
        // 默认顺序: Standard → ChineseSimplified → Extended → ChineseTraditional → Korean → NintendoExt
        var result = new List<SharedFontType>
        {
            SharedFontType.Standard,
            SharedFontType.ChineseSimplified,
            SharedFontType.ExtendedChineseSimplified,
            SharedFontType.ChineseTraditional,
            SharedFontType.Korean,
            SharedFontType.NintendoExt,
        };

        // 日语/英语环境: Standard 最优先 (已默认)
        // 中文环境: ChineseSimplified/ChineseTraditional 前移
        if (language == 2 || language == 3) // 简体中文/繁体中文
        {
            result.Remove(SharedFontType.ChineseSimplified);
            result.Remove(SharedFontType.ChineseTraditional);
            result.Insert(0, language == 2 ? SharedFontType.ChineseSimplified : SharedFontType.ChineseTraditional);
            result.Insert(1, language == 2 ? SharedFontType.ExtendedChineseSimplified : SharedFontType.ChineseSimplified);
        }
        else if (language == 6) // 韩文
        {
            result.Remove(SharedFontType.Korean);
            result.Insert(0, SharedFontType.Korean);
        }

        return result;
    }
}

/// <summary>
/// pl:u / pl:s / pl:a — 共享字体服务 (ISharedFontInAccess)
/// nn::pl::detail::ISharedFontInAccess
/// 几乎所有需要显示文字的 homebrew 都通过此服务获取系统共享字体
/// 提供字体加载请求、状态查询、内存映射信息
/// 命令表基于 SwitchBrew Shared_Font_services 页面和 Ryujinx 实现
/// </summary>
public abstract class PlServiceBase : IIpcService
{
    public abstract string PortName { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly PlState _state;

    protected PlServiceBase(PlState state)
    {
        _state = state;
        _commandTable = BuildCommandTable();
    }

    /// <summary>
    /// 构建命令表 — 子类可重写以添加额外命令 (如 pl:a 的 cmd 100)
    /// </summary>
    protected virtual Dictionary<uint, ServiceCommand> BuildCommandTable()
    {
        return new Dictionary<uint, ServiceCommand>
        {
            [0] = RequestLoad,                          // 请求加载字体
            [1] = GetLoadState,                         // 获取字体加载状态
            [2] = GetSize,                              // 获取字体数据大小
            [3] = GetSharedMemoryAddressOffset,         // 获取共享内存偏移
            [4] = GetSharedMemoryNativeHandle,          // 获取共享内存句柄
            [5] = GetSharedFontInOrderOfPriority,       // 按优先级获取字体列表
            [6] = GetSharedFontSeparateFontVfs,         // [5.0.0+] 获取独立字体 VFS
            [7] = GetSharedFontInOrderOfPriorityComplete, // [6.0.0+] 完整优先级列表
            [8] = GetSharedFontInOrderOfPriorityWithOs, // [8.0.0+] 带 OS 信息优先级列表
        };
    }

    /// <summary>命令 0: RequestLoad — 请求加载指定类型的字体</summary>
    /// <remarks>在模拟器中为空操作，字体数据已预加载</remarks>
    private ResultCode RequestLoad(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.PlResult(2);
        var fontType = (SharedFontType)BitConverter.ToUInt32(request.Data, 0);

        if (fontType >= SharedFontType.Count)
        {
            Logger.Warning(PortName, $"{PortName}: RequestLoad → invalid font type {fontType}");
            return ResultCode.PlResult(4); // InvalidFontType
        }

        _state.RequestLoad(fontType);
        Logger.Debug(PortName, $"{PortName}: RequestLoad({fontType})");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetLoadState — 获取字体加载状态</summary>
    /// <returns>uint32: 0=NotLoaded, 1=Loaded</returns>
    private ResultCode GetLoadState(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.PlResult(2);
        var fontType = (SharedFontType)BitConverter.ToUInt32(request.Data, 0);

        var info = _state.GetFontInfo(fontType);
        uint loadState = info != null && info.IsLoaded
            ? (uint)SharedFontLoadState.Loaded
            : (uint)SharedFontLoadState.NotLoaded;

        response.Data.AddRange(BitConverter.GetBytes(loadState));
        Logger.Debug(PortName, $"{PortName}: GetLoadState({fontType}) → {(SharedFontLoadState)loadState}");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetSize — 获取字体数据大小</summary>
    /// <returns>uint32: 字体数据字节数</returns>
    private ResultCode GetSize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.PlResult(2);
        var fontType = (SharedFontType)BitConverter.ToUInt32(request.Data, 0);

        var info = _state.GetFontInfo(fontType);
        if (info == null)
        {
            Logger.Warning(PortName, $"{PortName}: GetSize → unknown font type {fontType}");
            return ResultCode.PlResult(4);
        }

        response.Data.AddRange(BitConverter.GetBytes(info.Size));
        Logger.Debug(PortName, $"{PortName}: GetSize({fontType}) → 0x{info.Size:X8}");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetSharedMemoryAddressOffset — 获取字体在共享内存中的偏移</summary>
    /// <returns>uint64: 共享内存偏移量</returns>
    private ResultCode GetSharedMemoryAddressOffset(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.PlResult(2);
        var fontType = (SharedFontType)BitConverter.ToUInt32(request.Data, 0);

        var info = _state.GetFontInfo(fontType);
        if (info == null)
        {
            Logger.Warning(PortName, $"{PortName}: GetSharedMemoryAddressOffset → unknown font type {fontType}");
            return ResultCode.PlResult(4);
        }

        response.Data.AddRange(BitConverter.GetBytes(info.AddressOffset));
        Logger.Debug(PortName, $"{PortName}: GetSharedMemoryAddressOffset({fontType}) → 0x{info.AddressOffset:X16}");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetSharedMemoryNativeHandle — 获取共享内存区域的句柄</summary>
    /// <returns>共享内存 KSharedMemory 句柄</returns>
    private ResultCode GetSharedMemoryNativeHandle(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.SharedMemoryHandle));
        Logger.Debug(PortName, $"{PortName}: GetSharedMemoryNativeHandle → 0x{_state.SharedMemoryHandle:X8}");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetSharedFontInOrderOfPriority — 按优先级获取字体列表</summary>
    /// <remarks>
    /// 输入: uint32 language, uint64 aruid (PID)
    /// 输出: SharedFontType[6] 数组 + uint64 offset[6] + uint8 loaded[6] + int32 count
    /// 真实 Switch 根据语言和地区返回不同优先级的字体列表
    /// </remarks>
    private ResultCode GetSharedFontInOrderOfPriority(IpcRequest request, ref IpcResponse response)
    {
        // 本项目约定: PID descriptor 在 request.Data 偏移 0 (8 bytes)
        // 输入: uint64 pid (PID descriptor) + uint32 language
        uint language = 0; // 默认英语
        if (request.Data.Length >= 12)
        {
            language = BitConverter.ToUInt32(request.Data, 8); // PID 占 8 bytes, language 在 offset 8
        }

        var fonts = PlState.GetFontsInOrderOfPriority(language);

        // 输出格式:
        // SharedFontType[6] (4 bytes each = 24 bytes)
        for (int i = 0; i < 6; i++)
        {
            uint type = i < fonts.Count ? (uint)fonts[i] : 0xFFFFFFFF; // 0xFFFFFFFF = invalid
            response.Data.AddRange(BitConverter.GetBytes(type));
        }

        // AddressOffset[6] (8 bytes each = 48 bytes)
        for (int i = 0; i < 6; i++)
        {
            ulong offset = 0;
            if (i < fonts.Count)
            {
                var info = _state.GetFontInfo(fonts[i]);
                offset = info?.AddressOffset ?? 0;
            }
            response.Data.AddRange(BitConverter.GetBytes(offset));
        }

        // Loaded[6] (1 byte each = 6 bytes)
        for (int i = 0; i < 6; i++)
        {
            byte loaded = 0;
            if (i < fonts.Count)
            {
                var info = _state.GetFontInfo(fonts[i]);
                loaded = (byte)(info?.IsLoaded == true ? 1 : 0);
            }
            response.Data.Add(loaded);
        }

        // Count (int32)
        response.Data.AddRange(BitConverter.GetBytes(fonts.Count));

        Logger.Debug(PortName, $"{PortName}: GetSharedFontInOrderOfPriority(lang={language}) → {fonts.Count} fonts");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetSharedFontSeparateFontVfs — [5.0.0+] 获取独立字体 VFS (stub)</summary>
    private ResultCode GetSharedFontSeparateFontVfs(IpcRequest request, ref IpcResponse response)
    {
        // 返回空的 VFS 句柄 — 不支持独立字体 VFS
        int vfsHandle = unchecked((int)0xFFFF2010);
        response.Data.AddRange(BitConverter.GetBytes(vfsHandle));
        Logger.Debug(PortName, $"{PortName}: GetSharedFontSeparateFontVfs (stub) → VFS handle");
        return ResultCode.Success;
    }

    /// <summary>命令 7: GetSharedFontInOrderOfPriorityComplete — [6.0.0+] 完整优先级列表</summary>
    /// <remarks>
    /// 与 cmd 5 相同，但额外返回每种字体的文件路径信息
    /// 简化实现: 复用 cmd 5 的逻辑并追加空路径数据
    /// </remarks>
    private ResultCode GetSharedFontInOrderOfPriorityComplete(IpcRequest request, ref IpcResponse response)
    {
        // 先输出与 cmd 5 相同的优先级列表
        var result = GetSharedFontInOrderOfPriority(request, ref response);

        // 追加字体文件路径 (空路径 stub)
        for (int i = 0; i < 6; i++)
        {
            var pathBytes = new byte[0x80]; // 每个路径 128 bytes, UTF-16LE, null-terminated
            response.Data.AddRange(pathBytes);
        }

        Logger.Debug(PortName, $"{PortName}: GetSharedFontInOrderOfPriorityComplete (stub with empty paths)");
        return result;
    }

    /// <summary>命令 8: GetSharedFontInOrderOfPriorityWithOs — [8.0.0+] 带 OS 信息优先级列表 (stub)</summary>
    private ResultCode GetSharedFontInOrderOfPriorityWithOs(IpcRequest request, ref IpcResponse response)
    {
        // 与 cmd 5 相同，但附加 OS 标志位
        var result = GetSharedFontInOrderOfPriority(request, ref response);

        // 附加: int32 osFlags (0 = no OS-specific flags)
        response.Data.AddRange(BitConverter.GetBytes(0));

        Logger.Debug(PortName, $"{PortName}: GetSharedFontInOrderOfPriorityWithOs (stub)");
        return result;
    }

    internal PlState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>pl:u — 共享字体服务 (用户端口)</summary>
public sealed class PlUService : PlServiceBase
{
    public override string PortName => "pl:u";
    public PlUService(PlState state) : base(state) { }
}

/// <summary>pl:s — 共享字体服务 (系统端口)</summary>
public sealed class PlSService : PlServiceBase
{
    public override string PortName => "pl:s";
    public PlSService(PlState state) : base(state) { }
}

/// <summary>
/// pl:a — 共享字体服务 (管理员端口)
/// nn::pl::detail::ISharedFontInAccess (Admin)
/// 比 pl:u/pl:s 多出 SetSharedFontInAccess 等管理命令
/// 命令表基于 SwitchBrew Shared_Font_services 页面
/// </summary>
public sealed class PlAService : PlServiceBase
{
    public override string PortName => "pl:a";

    public PlAService(PlState state) : base(state) { }

    protected override Dictionary<uint, ServiceCommand> BuildCommandTable()
    {
        var table = base.BuildCommandTable();
        table[100] = SetSharedFontInAccess; // 设置共享字体访问权限
        return table;
    }

    /// <summary>命令 100: SetSharedFontInAccess — 设置共享字体访问权限 (stub)</summary>
    /// <remarks>管理员命令，用于强制刷新或重置字体加载状态</remarks>
    private ResultCode SetSharedFontInAccess(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: SetSharedFontInAccess (stub, admin-only)");
        return ResultCode.Success;
    }
}
