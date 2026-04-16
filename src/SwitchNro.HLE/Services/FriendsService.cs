using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的好友服务状态
/// </summary>
public sealed class FriendState
{
    /// <summary>好友列表 (NetworkServiceAccountId → FriendInfo)</summary>
    private readonly Dictionary<ulong, FriendInfo> _friends = new();

    /// <summary>是否在线</summary>
    private bool _online = true;

    /// <summary>好友列表是否已加载</summary>
    private bool _friendListLoaded;

    /// <summary>当前用户的游玩状态</summary>
    private FriendPlayTime _playTime = new() { TitleId = 0, Presence = FriendPresence.Offline };

    /// <summary>用户设置: 是否允许好友请求</summary>
    private bool _friendRequestAllowed = true;

    /// <summary>用户设置: 是否公开在线状态</summary>
    private bool _presenceVisible = true;

    /// <summary>用户设置: 是否公开游玩标题</summary>
    private bool _playTitleVisible = true;

    /// <summary>是否在线</summary>
    public bool Online
    {
        get => _online;
        set => _online = value;
    }

    /// <summary>好友列表是否已加载</summary>
    public bool FriendListLoaded
    {
        get => _friendListLoaded;
        set => _friendListLoaded = value;
    }

    /// <summary>是否允许好友请求</summary>
    public bool FriendRequestAllowed
    {
        get => _friendRequestAllowed;
        set => _friendRequestAllowed = value;
    }

    /// <summary>是否公开在线状态</summary>
    public bool PresenceVisible
    {
        get => _presenceVisible;
        set => _presenceVisible = value;
    }

    /// <summary>是否公开游玩标题</summary>
    public bool PlayTitleVisible
    {
        get => _playTitleVisible;
        set => _playTitleVisible = value;
    }

    /// <summary>当前游玩状态</summary>
    public FriendPlayTime PlayTime
    {
        get => _playTime;
        set => _playTime = value;
    }

    /// <summary>添加好友</summary>
    public void AddFriend(ulong accountId, string name, FriendPresence presence = FriendPresence.Offline)
    {
        _friends[accountId] = new FriendInfo
        {
            AccountId = accountId,
            Name = name,
            Presence = presence,
        };
    }

    /// <summary>移除好友</summary>
    public bool RemoveFriend(ulong accountId) => _friends.Remove(accountId);

    /// <summary>获取好友信息</summary>
    public FriendInfo? GetFriend(ulong accountId) =>
        _friends.TryGetValue(accountId, out var info) ? info : null;

    /// <summary>获取所有好友列表</summary>
    public IReadOnlyDictionary<ulong, FriendInfo> AllFriends => _friends;
}

/// <summary>好友信息</summary>
public sealed class FriendInfo
{
    public ulong AccountId { get; set; }
    public string Name { get; set; } = "";
    public FriendPresence Presence { get; set; }
}

/// <summary>好友在线状态</summary>
public enum FriendPresence : byte
{
    Offline = 0,
    Online = 1,
    OnlinePlay = 2,
}

/// <summary>游玩时间信息</summary>
public sealed class FriendPlayTime
{
    public ulong TitleId { get; set; }
    public FriendPresence Presence { get; set; }
}

/// <summary>
/// 好友服务创建器基类 — friend:u/v/m/s/a 共享的命令处理逻辑
/// nn::friends::detail::ipc::IServiceCreator
/// 五个端口命令表完全相同（仅 CreateFriendService），仅端口名和句柄基址不同
/// </summary>
public abstract class FriendServiceCreatorBase : IIpcService
{
    public abstract string PortName { get; }

    /// <summary>每端口唯一的句柄偏移量 (0~4)</summary>
    protected abstract int HandleOffset { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly FriendState _state;

    protected FriendServiceCreatorBase(FriendState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateFriendService,       // 创建 IFriendService（含用户 ID）
        };
    }

    /// <summary>命令 0: CreateFriendService — 创建 IFriendService</summary>
    private ResultCode CreateFriendService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.FriendResult(2); // Invalid size

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        int handle = unchecked((int)(0xFFFF0800 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: CreateFriendService(uid=0x{userId:X16}) → IFriendService handle");
        return ResultCode.Success;
    }

    internal FriendState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>friend:u — 好友服务 (用户端口)</summary>
public sealed class FriendUService : FriendServiceCreatorBase
{
    public override string PortName => "friend:u";
    protected override int HandleOffset => 0;
    public FriendUService(FriendState state) : base(state) { }
}

/// <summary>friend:v — 好友服务 (编辑端口)</summary>
public sealed class FriendVService : FriendServiceCreatorBase
{
    public override string PortName => "friend:v";
    protected override int HandleOffset => 1;
    public FriendVService(FriendState state) : base(state) { }
}

/// <summary>friend:m — 好友服务 (管理端口, 21.0.0+)</summary>
public sealed class FriendMService : FriendServiceCreatorBase
{
    public override string PortName => "friend:m";
    protected override int HandleOffset => 2;
    public FriendMService(FriendState state) : base(state) { }
}

/// <summary>friend:s — 好友服务 (系统端口)</summary>
public sealed class FriendSService : FriendServiceCreatorBase
{
    public override string PortName => "friend:s";
    protected override int HandleOffset => 3;
    public FriendSService(FriendState state) : base(state) { }
}

/// <summary>friend:a — 好友服务 (管理员端口)</summary>
public sealed class FriendAService : FriendServiceCreatorBase
{
    public override string PortName => "friend:a";
    protected override int HandleOffset => 4;
    public FriendAService(FriendState state) : base(state) { }
}

/// <summary>
/// IFriendService — 好友服务接口
/// nn::friends::detail::ipc::IFriendService
/// 通过 friend:u/v/m/s/a 的 CreateFriendService 获取
/// 提供好友列表管理、好友请求、在线状态等功能
/// 命令表基于 SwitchBrew Friend_services 页面
/// </summary>
public sealed class FriendService : IIpcService
{
    public string PortName => "friend:svc"; // 内部虚拟端口名 — 通过 CreateFriendService 获取，不注册为命名端口

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly FriendState _state;

    public FriendService(FriendState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]     = GetUserSetting,                    // 获取用户设置
            [1]     = GetFriendList,                     // 获取好友列表
            [2]     = UpdateFriendInfo,                  // 更新好友信息 (stub)
            [3]     = GetFriendProfileList,              // 获取好友 Profile 列表 (stub)
            [4]     = GetListParameter,                  // 获取列表参数 (stub)
            [5]     = CheckFriendListPermission,         // 检查好友列表权限 (stub)
            [10]    = GetFriendList,                     // 获取好友列表 (变体)
            [11]    = GetFriendInvitableList,            // 获取可邀请好友列表 (stub)
            [20]    = GetNewFriendFlag,                  // 获取新好友标志 (stub)
            [21]    = GetUnreadFriendMessageCount,      // 获取未读好友消息数 (stub)
            [30]    = GetReceivedFriendRequestList,     // 获取收到的好友请求列表 (stub)
            [31]    = GetBlockedFriendList,              // 获取被阻止的好友列表 (stub)
            [40]    = GetFriendNotificationEvent,       // 获取好友通知事件 (stub)
            [41]    = GetFriendInvitationNotificationEvent, // 获取好友邀请通知 (stub)
            [50]    = GetPlayTime,                       // 获取游玩时间
            [51]    = GetPlayTimeStart,                  // 获取游玩开始时间 (stub)
            [52]    = GetPlayTimeEnd,                    // 获取游玩结束时间 (stub)
            [60]    = GetLastOnlineTime,                 // 获取最后在线时间 (stub)
            [70]    = SendFriendRequest,                 // 发送好友请求 (stub)
            [71]    = CancelFriendRequest,              // 取消好友请求 (stub)
            [80]    = AcceptFriendRequest,               // 接受好友请求 (stub)
            [81]    = RejectFriendRequest,               // 拒绝好友请求 (stub)
            [90]    = RemoveFriend,                      // 删除好友
            [100]   = GetBlockedUserList,                // 获取阻止用户列表 (stub)
            [10500] = GetProfileList,                    // 获取 Profile 列表 (stub)
        };
    }

    /// <summary>命令 0: GetUserSetting — 获取用户设置</summary>
    private ResultCode GetUserSetting(IpcRequest request, ref IpcResponse response)
    {
        // UserSetting: friendRequestAllowed(u8) + presenceVisible(u8) + playTitleVisible(u8) + padding
        response.Data.Add((byte)(_state.FriendRequestAllowed ? 1 : 0));
        response.Data.Add((byte)(_state.PresenceVisible ? 1 : 0));
        response.Data.Add((byte)(_state.PlayTitleVisible ? 1 : 0));
        response.Data.Add(0); // padding
        Logger.Debug(nameof(FriendService), "friend:svc: GetUserSetting");
        return ResultCode.Success;
    }

    /// <summary>命令 1/10: GetFriendList — 获取好友列表</summary>
    private ResultCode GetFriendList(IpcRequest request, ref IpcResponse response)
    {
        // 返回好友数量 + 好友 ID 列表
        response.Data.AddRange(BitConverter.GetBytes(_state.AllFriends.Count));
        foreach (var (accountId, info) in _state.AllFriends)
        {
            // FriendBrief: AccountId(8) + Presence(u8) + padding(3) + NameOffset(4) = 16 bytes
            response.Data.AddRange(BitConverter.GetBytes(accountId));
            response.Data.Add((byte)info.Presence);
            response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
            response.Data.AddRange(BitConverter.GetBytes(0U)); // name offset
        }
        Logger.Debug(nameof(FriendService), $"friend:svc: GetFriendList → {_state.AllFriends.Count} friends");
        return ResultCode.Success;
    }

    /// <summary>命令 2: UpdateFriendInfo — 更新好友信息 (stub)</summary>
    private ResultCode UpdateFriendInfo(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FriendService), "friend:svc: UpdateFriendInfo (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetFriendProfileList — 获取好友 Profile 列表 (stub)</summary>
    private ResultCode GetFriendProfileList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetFriendProfileList → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetListParameter — 获取列表参数 (stub)</summary>
    private ResultCode GetListParameter(IpcRequest request, ref IpcResponse response)
    {
        // ListParameter: u8 mode + padding
        response.Data.Add(0); // mode = All
        response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
        Logger.Debug(nameof(FriendService), "friend:svc: GetListParameter (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 5: CheckFriendListPermission — 检查好友列表权限 (stub)</summary>
    private ResultCode CheckFriendListPermission(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(1U)); // permitted
        Logger.Debug(nameof(FriendService), "friend:svc: CheckFriendListPermission → true (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 11: GetFriendInvitableList — 获取可邀请好友列表 (stub)</summary>
    private ResultCode GetFriendInvitableList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetFriendInvitableList → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 20: GetNewFriendFlag — 获取新好友标志 (stub)</summary>
    private ResultCode GetNewFriendFlag(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // no new friends
        Logger.Debug(nameof(FriendService), "friend:svc: GetNewFriendFlag → false (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 21: GetUnreadFriendMessageCount — 获取未读好友消息数 (stub)</summary>
    private ResultCode GetUnreadFriendMessageCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // 0 unread
        Logger.Debug(nameof(FriendService), "friend:svc: GetUnreadFriendMessageCount → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 30: GetReceivedFriendRequestList — 获取收到的好友请求列表 (stub)</summary>
    private ResultCode GetReceivedFriendRequestList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetReceivedFriendRequestList → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 31: GetBlockedFriendList — 获取被阻止的好友列表 (stub)</summary>
    private ResultCode GetBlockedFriendList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetBlockedFriendList → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 40: GetFriendNotificationEvent — 获取好友通知事件 (stub)</summary>
    private ResultCode GetFriendNotificationEvent(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0820);
        response.Data.AddRange(BitConverter.GetBytes(handle)); // KEvent handle
        Logger.Debug(nameof(FriendService), "friend:svc: GetFriendNotificationEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 41: GetFriendInvitationNotificationEvent — 获取好友邀请通知 (stub)</summary>
    private ResultCode GetFriendInvitationNotificationEvent(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0821);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(FriendService), "friend:svc: GetFriendInvitationNotificationEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 50: GetPlayTime — 获取游玩时间</summary>
    private ResultCode GetPlayTime(IpcRequest request, ref IpcResponse response)
    {
        // PlayTime: TitleId(8) + Presence(u8) + padding(3) = 12 bytes
        response.Data.AddRange(BitConverter.GetBytes(_state.PlayTime.TitleId));
        response.Data.Add((byte)_state.PlayTime.Presence);
        response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
        Logger.Debug(nameof(FriendService), $"friend:svc: GetPlayTime → title=0x{_state.PlayTime.TitleId:X16}, presence={_state.PlayTime.Presence}");
        return ResultCode.Success;
    }

    /// <summary>命令 51: GetPlayTimeStart — 获取游玩开始时间 (stub)</summary>
    private ResultCode GetPlayTimeStart(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0UL)); // timestamp = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetPlayTimeStart → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 52: GetPlayTimeEnd — 获取游玩结束时间 (stub)</summary>
    private ResultCode GetPlayTimeEnd(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0UL));
        Logger.Debug(nameof(FriendService), "friend:svc: GetPlayTimeEnd → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 60: GetLastOnlineTime — 获取最后在线时间 (stub)</summary>
    private ResultCode GetLastOnlineTime(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));
        Logger.Debug(nameof(FriendService), "friend:svc: GetLastOnlineTime (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 70: SendFriendRequest — 发送好友请求 (stub)</summary>
    private ResultCode SendFriendRequest(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FriendService), "friend:svc: SendFriendRequest (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 71: CancelFriendRequest — 取消好友请求 (stub)</summary>
    private ResultCode CancelFriendRequest(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FriendService), "friend:svc: CancelFriendRequest (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 80: AcceptFriendRequest — 接受好友请求 (stub)</summary>
    private ResultCode AcceptFriendRequest(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FriendService), "friend:svc: AcceptFriendRequest (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 81: RejectFriendRequest — 拒绝好友请求 (stub)</summary>
    private ResultCode RejectFriendRequest(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FriendService), "friend:svc: RejectFriendRequest (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 90: RemoveFriend — 删除好友</summary>
    private ResultCode RemoveFriend(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.FriendResult(2);

        ulong accountId = BitConverter.ToUInt64(request.Data, 0);
        bool removed = _state.RemoveFriend(accountId);
        if (!removed)
            return ResultCode.FriendResult(6); // Friend not found

        Logger.Debug(nameof(FriendService), $"friend:svc: RemoveFriend(id=0x{accountId:X16}) → removed");
        return ResultCode.Success;
    }

    /// <summary>命令 100: GetBlockedUserList — 获取阻止用户列表 (stub)</summary>
    private ResultCode GetBlockedUserList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetBlockedUserList → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10500: GetProfileList — 获取 Profile 列表 (stub)</summary>
    private ResultCode GetProfileList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(FriendService), "friend:svc: GetProfileList → 0 (stub)");
        return ResultCode.Success;
    }

    internal FriendState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}
