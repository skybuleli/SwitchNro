using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 News 服务状态
/// </summary>
public sealed class NewsState
{
    /// <summary>新闻条目列表</summary>
    private readonly List<NewsEntry> _entries = new();

    /// <summary>是否有新到达的新闻</summary>
    private bool _newlyArrived;

    /// <summary>缓存的新闻条目序列化数据 (EntryId → byte buffer)。调用者不得修改返回的缓冲区。</summary>
    private readonly Dictionary<ulong, byte[]> _entryDataCache = new();

    /// <summary>获取所有新闻条目</summary>
    public IReadOnlyList<NewsEntry> Entries => _entries;

    /// <summary>是否有新到达的新闻</summary>
    public bool NewlyArrived
    {
        get => _newlyArrived;
        set => _newlyArrived = value;
    }

    /// <summary>添加新闻条目</summary>
    public void AddEntry(NewsEntry entry) => _entries.Add(entry);

    /// <summary>获取新闻条目数量</summary>
    public int GetCount() => _entries.Count;

    /// <summary>按条目 ID 查找新闻条目</summary>
    public NewsEntry? FindById(ulong id)
    {
        foreach (var e in _entries)
            if (e.Id == id) return e;
        return null;
    }

    /// <summary>获取缓存的新闻条目序列化数据；如不存在则构建并缓存。调用者不得修改返回的缓冲区。</summary>
    public byte[] GetEntryDataBuffer(ulong id)
    {
        if (_entryDataCache.TryGetValue(id, out var cached))
            return cached;
        var entry = FindById(id);
        if (entry == null) return Array.Empty<byte>();
        return BuildEntryDataBuffer(entry);
    }

    /// <summary>按标签筛选新闻条目</summary>
    public IReadOnlyList<NewsEntry> FindByTag(string tag)
    {
        var result = new List<NewsEntry>();
        foreach (var e in _entries)
            if (e.Tag == tag) result.Add(e);
        return result;
    }

    /// <summary>按标签统计新闻条目数量</summary>
    public uint CountByTag(string tag)
    {
        uint count = 0;
        foreach (var e in _entries)
            if (e.Tag == tag) count++;
        return count;
    }

    /// <summary>删除新闻条目并使缓存失效</summary>
    public bool RemoveEntry(ulong id)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Id == id)
            {
                _entries.RemoveAt(i);
                _entryDataCache.Remove(id);
                return true;
            }
        }
        return false;
    }

    /// <summary>构建并缓存新闻条目序列化数据</summary>
    /// <remarks>布局: Id(8) + Title(0x80 UTF-16LE) + Content(0x200 UTF-16LE) + Timestamp(8) + Tag(0x18 UTF-16LE) = 0x2A8 bytes</remarks>
    private byte[] BuildEntryDataBuffer(NewsEntry entry)
    {
        var buf = new byte[0x2A8];
        // Id (offset 0, 8 bytes)
        BitConverter.GetBytes(entry.Id).CopyTo(buf, 0);
        // Title (offset 8, 0x80 bytes, UTF-16LE padded)
        var titleBytes = Encoding.Unicode.GetBytes(entry.Title);
        Array.Copy(titleBytes, 0, buf, 8, Math.Min(titleBytes.Length, 0x80));
        // Content (offset 0x88, 0x200 bytes, UTF-16LE padded)
        var contentBytes = Encoding.Unicode.GetBytes(entry.Content);
        Array.Copy(contentBytes, 0, buf, 0x88, Math.Min(contentBytes.Length, 0x200));
        // Timestamp (offset 0x288, 8 bytes)
        BitConverter.GetBytes(entry.Timestamp).CopyTo(buf, 0x288);
        // Tag (offset 0x290, 0x18 bytes, UTF-16LE padded)
        var tagBytes = Encoding.Unicode.GetBytes(entry.Tag);
        Array.Copy(tagBytes, 0, buf, 0x290, Math.Min(tagBytes.Length, 0x18));
        _entryDataCache[entry.Id] = buf;
        return buf;
    }
}

/// <summary>新闻条目</summary>
public sealed class NewsEntry
{
    public ulong Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public long Timestamp { get; set; }
    public string Tag { get; set; } = "";
}

/// <summary>
/// News 服务创建器基类 — news:a/c/m/p/v 共享的命令处理逻辑
/// nn::news::detail::ipc::INewsService
/// 五个端口命令表完全相同，仅端口名和权限不同
/// 命令表基于 SwitchBrew News_services 页面
/// </summary>
public abstract class NewsServiceCreatorBase : IIpcService
{
    public abstract string PortName { get; }

    /// <summary>每端口唯一的句柄偏移量 (0~4)</summary>
    protected abstract int HandleOffset { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly NewsState _state;

    protected NewsServiceCreatorBase(NewsState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetNewsService,                    // 创建 INewsDataService
            [1] = GetNewsServiceForApplication,      // 创建 INewsDataService (应用专用)
            [2] = GetNewsDataService,                 // 创建 INewsDataService (变体)
            [3] = GetNewlyArrivedEventHolder,         // 创建 INewlyArrivedEventHolder
        };
    }

    /// <summary>命令 0: GetNewsService — 创建 INewsDataService</summary>
    private ResultCode GetNewsService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)(0xFFFF0B00 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetNewsService → INewsDataService handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetNewsServiceForApplication — 创建 INewsDataService (应用专用)</summary>
    private ResultCode GetNewsServiceForApplication(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)(0xFFFF0B10 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetNewsServiceForApplication → INewsDataService handle");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetNewsDataService — 创建 INewsDataService (变体)</summary>
    private ResultCode GetNewsDataService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)(0xFFFF0B20 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetNewsDataService → INewsDataService handle");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetNewlyArrivedEventHolder — 创建 INewlyArrivedEventHolder</summary>
    private ResultCode GetNewlyArrivedEventHolder(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)(0xFFFF0B30 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetNewlyArrivedEventHolder → handle");
        return ResultCode.Success;
    }

    internal NewsState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>news:a — News 服务 (管理员端口)</summary>
public sealed class NewsAService : NewsServiceCreatorBase
{
    public override string PortName => "news:a";
    protected override int HandleOffset => 0;
    public NewsAService(NewsState state) : base(state) { }
}

/// <summary>news:c — News 服务 (创建端口)</summary>
public sealed class NewsCService : NewsServiceCreatorBase
{
    public override string PortName => "news:c";
    protected override int HandleOffset => 1;
    public NewsCService(NewsState state) : base(state) { }
}

/// <summary>news:m — News 服务 (管理端口)</summary>
public sealed class NewsMService : NewsServiceCreatorBase
{
    public override string PortName => "news:m";
    protected override int HandleOffset => 2;
    public NewsMService(NewsState state) : base(state) { }
}

/// <summary>news:p — News 服务 (生产者端口)</summary>
public sealed class NewsPService : NewsServiceCreatorBase
{
    public override string PortName => "news:p";
    protected override int HandleOffset => 3;
    public NewsPService(NewsState state) : base(state) { }
}

/// <summary>news:v — News 服务 (查看端口)</summary>
public sealed class NewsVService : NewsServiceCreatorBase
{
    public override string PortName => "news:v";
    protected override int HandleOffset => 4;
    public NewsVService(NewsState state) : base(state) { }
}

/// <summary>
/// INewsDataService — 新闻数据服务接口
/// nn::news::detail::ipc::INewsDataService
/// 通过 news:a/c/m/p/v 的 GetNewsService 获取
/// 命令表基于 SwitchBrew News_services 页面及 Ryujinx 实现
/// </summary>
public sealed class NewsDataService : IIpcService
{
    public string PortName => "news:data"; // 内部虚拟端口名

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly NewsState _state;

    public NewsDataService(NewsState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetNewsCount,                              // 获取新闻数量
            [1] = GetNews,                                   // 获取新闻条目 (stub)
            [2] = GetNewsCountForApplication,                // 获取应用新闻数量 (stub)
            [3] = GetNewsForApplication,                     // 获取应用新闻条目 (stub)
            [4] = GetNewsCountForApplicationWithTag,         // 获取带标签应用新闻数量 (stub)
            [5] = GetNewsForApplicationWithTag,              // 获取带标签应用新闻条目 (stub)
            [6] = GetNewsCountForApplicationWithTagAndLanguage, // 获取带标签语言新闻数量 (stub)
            [7] = GetNewsForApplicationWithTagAndLanguage,   // 获取带标签语言新闻条目 (stub)
        };
    }

    /// <summary>命令 0: GetNewsCount — 获取新闻数量</summary>
    private ResultCode GetNewsCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.GetCount()));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsCount → {_state.GetCount()}");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetNews — 获取新闻条目</summary>
    private ResultCode GetNews(IpcRequest request, ref IpcResponse response)
    {
        var entries = _state.Entries;
        response.Data.AddRange(BitConverter.GetBytes((uint)entries.Count));
        foreach (var entry in entries)
            response.Data.AddRange(_state.GetEntryDataBuffer(entry.Id));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNews → {entries.Count} entries");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetNewsCountForApplication — 获取应用新闻数量</summary>
    private ResultCode GetNewsCountForApplication(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NewsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        string tag = programId.ToString("X16", CultureInfo.InvariantCulture);
        uint count = _state.CountByTag(tag);
        response.Data.AddRange(BitConverter.GetBytes(count));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsCountForApplication(id=0x{programId:X16}) → {count}");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetNewsForApplication — 获取应用新闻条目</summary>
    private ResultCode GetNewsForApplication(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NewsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        string tag = programId.ToString("X16", CultureInfo.InvariantCulture);
        var matching = _state.FindByTag(tag);
        response.Data.AddRange(BitConverter.GetBytes((uint)matching.Count));
        foreach (var entry in matching)
            response.Data.AddRange(_state.GetEntryDataBuffer(entry.Id));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsForApplication(id=0x{programId:X16}) → {matching.Count} entries");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetNewsCountForApplicationWithTag — 获取带标签应用新闻数量</summary>
    private ResultCode GetNewsCountForApplicationWithTag(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8 + 0x20) return ResultCode.NewsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        string tag = Encoding.Unicode.GetString(request.Data, 8, 0x18).TrimEnd('\0');
        uint count = _state.CountByTag(tag);
        response.Data.AddRange(BitConverter.GetBytes(count));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsCountForApplicationWithTag(id=0x{programId:X16}, tag='{tag}') → {count}");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetNewsForApplicationWithTag — 获取带标签应用新闻条目</summary>
    private ResultCode GetNewsForApplicationWithTag(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8 + 0x20) return ResultCode.NewsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        string tag = Encoding.Unicode.GetString(request.Data, 8, 0x18).TrimEnd('\0');
        var matching = _state.FindByTag(tag);
        response.Data.AddRange(BitConverter.GetBytes((uint)matching.Count));
        foreach (var entry in matching)
            response.Data.AddRange(_state.GetEntryDataBuffer(entry.Id));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsForApplicationWithTag(id=0x{programId:X16}, tag='{tag}') → {matching.Count} entries");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetNewsCountForApplicationWithTagAndLanguage — 获取带标签语言新闻数量</summary>
    private ResultCode GetNewsCountForApplicationWithTagAndLanguage(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8 + 0x20 + 8) return ResultCode.NewsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        string tag = Encoding.Unicode.GetString(request.Data, 8, 0x18).TrimEnd('\0');
        // Language code is at offset 8+0x20 but filtering by language is not supported — count all matching tag
        uint count = _state.CountByTag(tag);
        response.Data.AddRange(BitConverter.GetBytes(count));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsCountForApplicationWithTagAndLanguage(id=0x{programId:X16}, tag='{tag}') → {count}");
        return ResultCode.Success;
    }

    /// <summary>命令 7: GetNewsForApplicationWithTagAndLanguage — 获取带标签语言新闻条目</summary>
    private ResultCode GetNewsForApplicationWithTagAndLanguage(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8 + 0x20 + 8) return ResultCode.NewsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        string tag = Encoding.Unicode.GetString(request.Data, 8, 0x18).TrimEnd('\0');
        var matching = _state.FindByTag(tag);
        response.Data.AddRange(BitConverter.GetBytes((uint)matching.Count));
        foreach (var entry in matching)
            response.Data.AddRange(_state.GetEntryDataBuffer(entry.Id));
        Logger.Debug(nameof(NewsDataService), $"news:data: GetNewsForApplicationWithTagAndLanguage(id=0x{programId:X16}, tag='{tag}') → {matching.Count} entries");
        return ResultCode.Success;
    }

    internal NewsState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// INewlyArrivedEventHolder — 新到达事件持有器
/// nn::news::detail::ipc::INewlyArrivedEventHolder
/// </summary>
public sealed class NewlyArrivedEventHolder : IIpcService
{
    public string PortName => "news:evnt"; // 内部虚拟端口名

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public NewlyArrivedEventHolder()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetEvent,      // 获取新到达通知事件
            [1] = TryPopEvent,   // 尝试弹出事件 (stub)
        };
    }

    /// <summary>命令 0: GetEvent — 获取新到达通知事件</summary>
    private ResultCode GetEvent(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0B40);
        response.Data.AddRange(BitConverter.GetBytes(handle)); // KEvent handle
        Logger.Debug(nameof(NewlyArrivedEventHolder), "news:evnt: GetEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 1: TryPopEvent — 尝试弹出事件 (stub)</summary>
    private ResultCode TryPopEvent(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // no events
        Logger.Debug(nameof(NewlyArrivedEventHolder), "news:evnt: TryPopEvent → 0 (stub)");
        return ResultCode.Success;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
