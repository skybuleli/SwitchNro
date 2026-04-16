using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的账户状态 — 管理用户列表和活跃用户
/// </summary>
public sealed class AccountState
{
    /// <summary>预定义的默认用户 ID</summary>
    private static readonly ulong DefaultUserId = 0x0000000000000001;

    /// <summary>所有已注册用户 ID 列表</summary>
    private readonly List<ulong> _allUsers = new() { DefaultUserId };

    /// <summary>当前已打开（登录）的用户 ID 集合</summary>
    private readonly HashSet<ulong> _openUsers = new() { DefaultUserId };

    /// <summary>用户昵称映射 (UserId → Nickname)</summary>
    private readonly Dictionary<ulong, string> _nicknames = new() { [DefaultUserId] = "Player" };

    /// <summary>用户头像数据映射 (UserId → 图片字节)</summary>
    private readonly Dictionary<ulong, byte[]> _profileImages = new();

    /// <summary>下一个注册用户 ID</summary>
    private ulong _nextUserId = 0x0000000000000002;

    /// <summary>是否允许用户注册请求</summary>
    private bool _registrationPermitted = true;

    /// <summary>获取所有用户列表</summary>
    public IReadOnlyList<ulong> AllUsers => _allUsers;

    /// <summary>获取已打开用户集合（无序）</summary>
    public IReadOnlySet<ulong> OpenUsers => _openUsers;

    /// <summary>获取已打开用户列表（按 ID 排序，保证确定性迭代顺序）</summary>
    public IReadOnlyList<ulong> OpenUsersOrdered => _openUsers.OrderBy(u => u).ToList();

    /// <summary>获取/设置注册许可</summary>
    public bool RegistrationPermitted
    {
        get => _registrationPermitted;
        set => _registrationPermitted = value;
    }

    /// <summary>注册新用户，返回用户 ID</summary>
    public ulong RegisterUser(string nickname = "NewUser")
    {
        var uid = _nextUserId++;
        _allUsers.Add(uid);
        _openUsers.Add(uid);
        _nicknames[uid] = nickname;
        return uid;
    }

    /// <summary>打开（登录）指定用户</summary>
    public bool OpenUser(ulong userId)
    {
        if (!_allUsers.Contains(userId)) return false;
        _openUsers.Add(userId);
        return true;
    }

    /// <summary>关闭（登出）指定用户</summary>
    public bool CloseUser(ulong userId)
    {
        return _openUsers.Remove(userId);
    }

    /// <summary>检查用户是否存在</summary>
    public bool UserExists(ulong userId) => _allUsers.Contains(userId);

    /// <summary>获取用户昵称</summary>
    public string? GetNickname(ulong userId) =>
        _nicknames.TryGetValue(userId, out var name) ? name : null;

    /// <summary>设置用户昵称</summary>
    public void SetNickname(ulong userId, string nickname) => _nicknames[userId] = nickname;

    /// <summary>设置用户头像</summary>
    public void SetProfileImage(ulong userId, byte[] imageData) => _profileImages[userId] = imageData;

    /// <summary>获取用户头像大小</summary>
    public int GetProfileImageSize(ulong userId) =>
        _profileImages.TryGetValue(userId, out var img) ? img.Length : 0;

    /// <summary>加载用户头像数据</summary>
    public byte[]? LoadProfileImage(ulong userId) =>
        _profileImages.TryGetValue(userId, out var img) ? img : null;
}

/// <summary>
/// 账户应用服务基类 — acc:u0 / acc:u1 共享的命令处理逻辑
/// nn::account::IAccountServiceForApplication / IAccountServiceForSystem
/// 两者命令表完全相同，仅端口名不同
/// 命令表基于 SwitchBrew Account_services 页面
/// </summary>
public abstract class AccApplicationServiceBase : IIpcService
{
    public abstract string PortName { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly AccountState _state;

    protected AccApplicationServiceBase(AccountState state)
    {
        _state = state;
        _commandTable = BuildCommandTable();
    }

    private Dictionary<uint, ServiceCommand> BuildCommandTable()
    {
        return new Dictionary<uint, ServiceCommand>
        {
            [0]   = GetUserCount,
            [1]   = GetUserExistence,
            [2]   = ListAllUsers,
            [3]   = ListOpenUsers,
            [4]   = GetLastOpenedUser,
            [5]   = GetProfile,
            [6]   = GetProfileDigest,
            [50]  = IsUserRegistrationRequestPermitted,
            [51]  = TrySelectUserWithoutInteractionDeprecated,
            [100] = GetUserRegistrationNotifier,
            [101] = GetUserStateChangeNotifier,
            [110] = StoreSaveDataThumbnail,
            [111] = ClearSaveDataThumbnail,
            [112] = LoadSaveDataThumbnail,
        };
    }

    /// <summary>命令 0: GetUserCount — 获取用户总数</summary>
    private ResultCode GetUserCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.AllUsers.Count));
        Logger.Debug(PortName, $"{PortName}: GetUserCount → {_state.AllUsers.Count}");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetUserExistence — 检查用户是否存在</summary>
    private ResultCode GetUserExistence(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2); // Invalid size

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        bool exists = _state.UserExists(userId);
        response.Data.AddRange(BitConverter.GetBytes(exists ? 1U : 0U));
        Logger.Debug(PortName, $"{PortName}: GetUserExistence(uid=0x{userId:X16}) → {exists}");
        return ResultCode.Success;
    }

    /// <summary>命令 2: ListAllUsers — 列出所有用户 ID</summary>
    private ResultCode ListAllUsers(IpcRequest request, ref IpcResponse response)
    {
        foreach (var uid in _state.AllUsers)
            response.Data.AddRange(BitConverter.GetBytes(uid));
        Logger.Debug(PortName, $"{PortName}: ListAllUsers → {_state.AllUsers.Count} users");
        return ResultCode.Success;
    }

    /// <summary>命令 3: ListOpenUsers — 列出已打开（登录）的用户 ID</summary>
    private ResultCode ListOpenUsers(IpcRequest request, ref IpcResponse response)
    {
        foreach (var uid in _state.OpenUsersOrdered)
            response.Data.AddRange(BitConverter.GetBytes(uid));
        Logger.Debug(PortName, $"{PortName}: ListOpenUsers → {_state.OpenUsers.Count} users");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetLastOpenedUser — 获取最后打开的用户 ID</summary>
    private ResultCode GetLastOpenedUser(IpcRequest request, ref IpcResponse response)
    {
        ulong lastUser = _state.AllUsers.Count > 0 ? _state.AllUsers[0] : 0;
        response.Data.AddRange(BitConverter.GetBytes(lastUser));
        Logger.Debug(PortName, $"{PortName}: GetLastOpenedUser → 0x{lastUser:X16}");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetProfile — 获取用户 Profile（返回 IProfile 句柄）</summary>
    private ResultCode GetProfile(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2);

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        if (!_state.UserExists(userId))
            return ResultCode.AccResult(4); // User not found

        int handle = unchecked((int)0xFFFF0600);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetProfile(uid=0x{userId:X16}) → IProfile handle");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetProfileDigest — 获取 Profile 摘要</summary>
    private ResultCode GetProfileDigest(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2);

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        if (!_state.UserExists(userId))
            return ResultCode.AccResult(4);

        // ProfileDigest: UserId(8) + LastEditTimestamp(8) + NicknameSize(4) + padding = 20 bytes
        response.Data.AddRange(BitConverter.GetBytes(userId));
        response.Data.AddRange(BitConverter.GetBytes(0UL)); // timestamp = 0
        var nickname = _state.GetNickname(userId) ?? "";
        response.Data.AddRange(BitConverter.GetBytes((uint)Encoding.UTF8.GetByteCount(nickname)));
        Logger.Debug(PortName, $"{PortName}: GetProfileDigest(uid=0x{userId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 50: IsUserRegistrationRequestPermitted — 是否允许注册</summary>
    private ResultCode IsUserRegistrationRequestPermitted(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.RegistrationPermitted ? 1U : 0U));
        Logger.Debug(PortName, $"{PortName}: IsUserRegistrationRequestPermitted → {_state.RegistrationPermitted}");
        return ResultCode.Success;
    }

    /// <summary>命令 51: TrySelectUserWithoutInteractionDeprecated — 无交互选择用户</summary>
    private ResultCode TrySelectUserWithoutInteractionDeprecated(IpcRequest request, ref IpcResponse response)
    {
        ulong userId = _state.AllUsers.Count > 0 ? _state.AllUsers[0] : 0;
        response.Data.AddRange(BitConverter.GetBytes(userId));
        Logger.Debug(PortName, $"{PortName}: TrySelectUserWithoutInteraction → default user");
        return ResultCode.Success;
    }

    /// <summary>命令 100: GetUserRegistrationNotifier — 获取用户注册通知器</summary>
    private ResultCode GetUserRegistrationNotifier(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0610);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetUserRegistrationNotifier → INotifier handle");
        return ResultCode.Success;
    }

    /// <summary>命令 101: GetUserStateChangeNotifier — 获取用户状态变更通知器</summary>
    private ResultCode GetUserStateChangeNotifier(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0611);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: GetUserStateChangeNotifier → INotifier handle");
        return ResultCode.Success;
    }

    /// <summary>命令 110: StoreSaveDataThumbnail — 存储备档缩略图 (stub)</summary>
    private ResultCode StoreSaveDataThumbnail(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: StoreSaveDataThumbnail (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 111: ClearSaveDataThumbnail — 清除存档缩略图 (stub)</summary>
    private ResultCode ClearSaveDataThumbnail(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ClearSaveDataThumbnail (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 112: LoadSaveDataThumbnail — 加载存档缩略图 (stub)</summary>
    private ResultCode LoadSaveDataThumbnail(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: LoadSaveDataThumbnail (stub)");
        return ResultCode.Success;
    }

    internal AccountState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// acc:u0 — 账户服务 (应用端口)
/// </summary>
public sealed class AccU0Service : AccApplicationServiceBase
{
    public override string PortName => "acc:u0";

    public AccU0Service(AccountState state) : base(state) { }
}

/// <summary>
/// acc:u1 — 账户服务 (系统应用端口)
/// 与 acc:u0 命令表完全相同，仅端口名不同
/// </summary>
public sealed class AccU1Service : AccApplicationServiceBase
{
    public override string PortName => "acc:u1";

    public AccU1Service(AccountState state) : base(state) { }
}

/// <summary>
/// acc:su — 账户服务 (系统管理端口)
/// nn::account::IAccountServiceForAdministrator
/// 提供完整的系统级账户管理功能，包括用户注册/注销等
/// </summary>
public sealed class AccSuService : IIpcService
{
    public string PortName => "acc:su";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly AccountState _state;

    public AccSuService(AccountState state)
    {
        _state = state;
        _commandTable = BuildCommandTable();
    }

    private Dictionary<uint, ServiceCommand> BuildCommandTable()
    {
        return new Dictionary<uint, ServiceCommand>
        {
            [0]   = GetUserCount,
            [1]   = GetUserExistence,
            [2]   = ListAllUsers,
            [3]   = ListOpenUsers,
            [4]   = GetLastOpenedUser,
            [5]   = GetProfile,
            [6]   = GetProfileDigest,
            [50]  = IsUserRegistrationRequestPermitted,
            [51]  = TrySelectUserWithoutInteractionDeprecated,
            [100] = GetUserRegistrationNotifier,
            [101] = GetUserStateChangeNotifier,
            [102] = GetBaasAccountManagerForSystemService,
            [103] = GetBaasUserAvailabilityChangeNotifier,
            [110] = StoreSaveDataThumbnail,
            [111] = ClearSaveDataThumbnail,
            [112] = LoadSaveDataThumbnail,
            [113] = GetSaveDataThumbnailExistence,
            [120] = ListOpenUsersInApplication,
            [130] = ActivateOpenContextRetention,
        };
    }

    private ResultCode GetUserCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.AllUsers.Count));
        return ResultCode.Success;
    }

    private ResultCode GetUserExistence(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.AccResult(2);
        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        response.Data.AddRange(BitConverter.GetBytes(_state.UserExists(userId) ? 1U : 0U));
        return ResultCode.Success;
    }

    private ResultCode ListAllUsers(IpcRequest request, ref IpcResponse response)
    {
        foreach (var uid in _state.AllUsers) response.Data.AddRange(BitConverter.GetBytes(uid));
        return ResultCode.Success;
    }

    private ResultCode ListOpenUsers(IpcRequest request, ref IpcResponse response)
    {
        foreach (var uid in _state.OpenUsersOrdered) response.Data.AddRange(BitConverter.GetBytes(uid));
        return ResultCode.Success;
    }

    private ResultCode GetLastOpenedUser(IpcRequest request, ref IpcResponse response)
    {
        ulong lastUser = _state.AllUsers.Count > 0 ? _state.AllUsers[0] : 0;
        response.Data.AddRange(BitConverter.GetBytes(lastUser));
        return ResultCode.Success;
    }

    private ResultCode GetProfile(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.AccResult(2);
        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        if (!_state.UserExists(userId)) return ResultCode.AccResult(4);
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0600)));
        return ResultCode.Success;
    }

    private ResultCode GetProfileDigest(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.AccResult(2);
        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        if (!_state.UserExists(userId)) return ResultCode.AccResult(4);
        response.Data.AddRange(BitConverter.GetBytes(userId));
        response.Data.AddRange(BitConverter.GetBytes(0UL));
        var nickname = _state.GetNickname(userId) ?? "";
        response.Data.AddRange(BitConverter.GetBytes((uint)Encoding.UTF8.GetByteCount(nickname)));
        return ResultCode.Success;
    }

    private ResultCode IsUserRegistrationRequestPermitted(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.RegistrationPermitted ? 1U : 0U));
        return ResultCode.Success;
    }

    private ResultCode TrySelectUserWithoutInteractionDeprecated(IpcRequest request, ref IpcResponse response)
    {
        ulong userId = _state.AllUsers.Count > 0 ? _state.AllUsers[0] : 0;
        response.Data.AddRange(BitConverter.GetBytes(userId));
        return ResultCode.Success;
    }

    private ResultCode GetUserRegistrationNotifier(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0610)));
        return ResultCode.Success;
    }

    private ResultCode GetUserStateChangeNotifier(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0611)));
        return ResultCode.Success;
    }

    /// <summary>命令 102: GetBaasAccountManagerForSystemService — 获取 BAAS 管理器 (stub)</summary>
    private ResultCode GetBaasAccountManagerForSystemService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.AccResult(2);
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0612)));
        Logger.Debug(nameof(AccSuService), "acc:su: GetBaasAccountManagerForSystemService → stub");
        return ResultCode.Success;
    }

    /// <summary>命令 103: GetBaasUserAvailabilityChangeNotifier — 获取 BAAS 可用性通知器 (stub)</summary>
    private ResultCode GetBaasUserAvailabilityChangeNotifier(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0613)));
        Logger.Debug(nameof(AccSuService), "acc:su: GetBaasUserAvailabilityChangeNotifier → stub");
        return ResultCode.Success;
    }

    private ResultCode StoreSaveDataThumbnail(IpcRequest request, ref IpcResponse response) => ResultCode.Success;
    private ResultCode ClearSaveDataThumbnail(IpcRequest request, ref IpcResponse response) => ResultCode.Success;
    private ResultCode LoadSaveDataThumbnail(IpcRequest request, ref IpcResponse response) => ResultCode.Success;

    /// <summary>命令 113: GetSaveDataThumbnailExistence — 检查存档缩略图是否存在 (stub)</summary>
    private ResultCode GetSaveDataThumbnailExistence(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // 不存在
        Logger.Debug(nameof(AccSuService), "acc:su: GetSaveDataThumbnailExistence → false");
        return ResultCode.Success;
    }

    /// <summary>命令 120: ListOpenUsersInApplication — 列出应用中已打开的用户</summary>
    private ResultCode ListOpenUsersInApplication(IpcRequest request, ref IpcResponse response)
    {
        foreach (var uid in _state.OpenUsersOrdered) response.Data.AddRange(BitConverter.GetBytes(uid));
        Logger.Debug(nameof(AccSuService), $"acc:su: ListOpenUsersInApplication → {_state.OpenUsers.Count} users");
        return ResultCode.Success;
    }

    /// <summary>命令 130: ActivateOpenContextRetention — 激活开放上下文保留 (stub)</summary>
    private ResultCode ActivateOpenContextRetention(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(unchecked((int)0xFFFF0614)));
        Logger.Debug(nameof(AccSuService), "acc:su: ActivateOpenContextRetention → stub");
        return ResultCode.Success;
    }

    internal AccountState State => _state;

    public void Dispose() { }
}

/// <summary>
/// IProfile — 用户档案服务 (nn::account::IProfile)
/// 通过 acc:u0/u1/su 的 GetProfile 获取，提供用户信息查询
/// </summary>
public sealed class AccountProfileService : IIpcService
{
    public string PortName => "acc:prof"; // 内部虚拟端口名 — 通过 GetProfile 获取，不注册为命名端口

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly AccountState _state;

    public AccountProfileService(AccountState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = Get,           // 获取完整 Profile
            [1] = GetBase,       // 获取基础 Profile
            [2] = GetImageSize,  // 获取头像大小
            [3] = LoadImage,     // 加载头像数据
        };
    }

    /// <summary>命令 0: Get — 获取完整用户 Profile</summary>
    private ResultCode Get(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2);

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        if (!_state.UserExists(userId))
            return ResultCode.AccResult(4);

        // UserProfile: UserId(8) + LastEditTimestamp(8) + NicknameSize(4) + Nickname(0x21 bytes) + padding = 0x2D bytes
        response.Data.AddRange(BitConverter.GetBytes(userId));
        response.Data.AddRange(BitConverter.GetBytes(0UL)); // timestamp
        var nickname = _state.GetNickname(userId) ?? "";
        var nicknameBytes = Encoding.UTF8.GetBytes(nickname);
        response.Data.AddRange(BitConverter.GetBytes((uint)nicknameBytes.Length));
        var paddedName = new byte[0x21];
        Array.Copy(nicknameBytes, paddedName, Math.Min(nicknameBytes.Length, 0x21));
        response.Data.AddRange(paddedName);
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetBase — 获取基础 Profile（不含头像）</summary>
    private ResultCode GetBase(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2);

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        if (!_state.UserExists(userId))
            return ResultCode.AccResult(4);

        // UserBase: UserId(8) + LastEditTimestamp(8) + NicknameSize(4) + Nickname(0x21 bytes) = 0x2D bytes
        response.Data.AddRange(BitConverter.GetBytes(userId));
        response.Data.AddRange(BitConverter.GetBytes(0UL));
        var nickname = _state.GetNickname(userId) ?? "";
        var nicknameBytes = Encoding.UTF8.GetBytes(nickname);
        response.Data.AddRange(BitConverter.GetBytes((uint)nicknameBytes.Length));
        var paddedName = new byte[0x21];
        Array.Copy(nicknameBytes, paddedName, Math.Min(nicknameBytes.Length, 0x21));
        response.Data.AddRange(paddedName);
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetImageSize — 获取头像图片大小</summary>
    private ResultCode GetImageSize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2);

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        int size = _state.GetProfileImageSize(userId);
        response.Data.AddRange(BitConverter.GetBytes((uint)size));
        return ResultCode.Success;
    }

    /// <summary>命令 3: LoadImage — 加载头像图片数据</summary>
    private ResultCode LoadImage(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.AccResult(2);

        ulong userId = BitConverter.ToUInt64(request.Data, 0);
        var imageData = _state.LoadProfileImage(userId);
        if (imageData == null)
            return ResultCode.AccResult(6); // No image

        response.Data.AddRange(imageData);
        response.Data.AddRange(BitConverter.GetBytes((uint)imageData.Length)); // bytesRead
        return ResultCode.Success;
    }

    internal AccountState State => _state;

    public void Dispose() { }
}
