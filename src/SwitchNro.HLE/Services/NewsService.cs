using System;
using System.Collections.Generic;
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

    /// <summary>命令 1: GetNews — 获取新闻条目 (stub)</summary>
    private ResultCode GetNews(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(NewsDataService), "news:data: GetNews → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetNewsCountForApplication — 获取应用新闻数量 (stub)</summary>
    private ResultCode GetNewsCountForApplication(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(NewsDataService), "news:data: GetNewsCountForApplication → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetNewsForApplication — 获取应用新闻条目 (stub)</summary>
    private ResultCode GetNewsForApplication(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(NewsDataService), "news:data: GetNewsForApplication → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetNewsCountForApplicationWithTag — 获取带标签应用新闻数量 (stub)</summary>
    private ResultCode GetNewsCountForApplicationWithTag(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(NewsDataService), "news:data: GetNewsCountForApplicationWithTag → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetNewsForApplicationWithTag — 获取带标签应用新闻条目 (stub)</summary>
    private ResultCode GetNewsForApplicationWithTag(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(NewsDataService), "news:data: GetNewsForApplicationWithTag → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetNewsCountForApplicationWithTagAndLanguage — 获取带标签语言新闻数量 (stub)</summary>
    private ResultCode GetNewsCountForApplicationWithTagAndLanguage(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(NewsDataService), "news:data: GetNewsCountForApplicationWithTagAndLanguage → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 7: GetNewsForApplicationWithTagAndLanguage — 获取带标签语言新闻条目 (stub)</summary>
    private ResultCode GetNewsForApplicationWithTagAndLanguage(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(nameof(NewsDataService), "news:data: GetNewsForApplicationWithTagAndLanguage → 0 (stub)");
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
