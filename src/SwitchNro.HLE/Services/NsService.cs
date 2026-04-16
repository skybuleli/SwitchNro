using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的应用管理器状态
/// </summary>
public sealed class NsAppManagerState
{
    /// <summary>已安装的应用记录列表</summary>
    private readonly List<ApplicationRecord> _records = new();

    /// <summary>下一个应用记录 ID</summary>
    private ulong _nextRecordId = 0x0100000000000000;

    /// <summary>缓存的 NACP 控制数据 (ProgramId → 0x4000 byte buffer)</summary>
    private readonly Dictionary<ulong, byte[]> _nacpCache = new();

    /// <summary>缓存的 DisplayInfo 名称数据 (ProgramId → 0x200 byte padded UTF-16LE name)</summary>
    private readonly Dictionary<ulong, byte[]> _displayNameCache = new();

    /// <summary>获取所有应用记录</summary>
    public IReadOnlyList<ApplicationRecord> Records => _records;

    /// <summary>注册新应用记录</summary>
    public ApplicationRecord RegisterRecord(ulong programId, string name, ApplicationRecordType type = ApplicationRecordType.Game)
    {
        var record = new ApplicationRecord
        {
            RecordId = _nextRecordId++,
            ProgramId = programId,
            Name = name,
            Type = type,
            InstalledSize = 0,
            Version = 0,
        };
        _records.Add(record);
        return record;
    }

    /// <summary>按 ProgramId 查找应用记录</summary>
    public ApplicationRecord? FindByProgramId(ulong programId)
    {
        foreach (var r in _records)
            if (r.ProgramId == programId) return r;
        return null;
    }

    /// <summary>获取缓存的 NACP 数据；如不存在则构建并缓存。调用者不得修改返回的缓冲区。</summary>
    public byte[] GetNacpBuffer(ulong programId)
    {
        if (_nacpCache.TryGetValue(programId, out var cached))
            return cached;
        var record = FindByProgramId(programId);
        if (record == null) return Array.Empty<byte>();
        return BuildNacpBuffer(record);
    }

    /// <summary>获取缓存的 DisplayInfo 名称数据；如不存在则构建并缓存。调用者不得修改返回的缓冲区。</summary>
    public byte[] GetDisplayNameBuffer(ulong programId)
    {
        if (_displayNameCache.TryGetValue(programId, out var cached))
            return cached;
        var record = FindByProgramId(programId);
        if (record == null) return Array.Empty<byte>();
        return BuildDisplayNameBuffer(record);
    }

    /// <summary>删除应用记录</summary>
    public bool RemoveRecord(ulong programId)
    {
        for (int i = 0; i < _records.Count; i++)
        {
            if (_records[i].ProgramId == programId)
            {
                _records.RemoveAt(i);
                _nacpCache.Remove(programId);
                _displayNameCache.Remove(programId);
                return true;
            }
        }
        return false;
    }

    /// <summary>构建并缓存 NACP 控制数据 (0x4000 bytes)</summary>
    private byte[] BuildNacpBuffer(ApplicationRecord record)
    {
        var nacp = new byte[0x4000];
        var nameBytes = Encoding.Unicode.GetBytes(record.Name);
        Array.Copy(nameBytes, nacp, Math.Min(nameBytes.Length, 0x200));
        _nacpCache[record.ProgramId] = nacp;
        return nacp;
    }

    /// <summary>构建并缓存 DisplayInfo 名称数据 (0x200 bytes, UTF-16LE padded)</summary>
    private byte[] BuildDisplayNameBuffer(ApplicationRecord record)
    {
        var paddedName = new byte[0x200];
        var nameBytes = Encoding.Unicode.GetBytes(record.Name);
        Array.Copy(nameBytes, paddedName, Math.Min(nameBytes.Length, 0x200));
        _displayNameCache[record.ProgramId] = paddedName;
        return paddedName;
    }
}

/// <summary>应用记录</summary>
public sealed class ApplicationRecord
{
    public ulong RecordId { get; set; }
    public ulong ProgramId { get; set; }
    public string Name { get; set; } = "";
    public ApplicationRecordType Type { get; set; }
    public ulong InstalledSize { get; set; }
    public uint Version { get; set; }
}

/// <summary>应用记录类型</summary>
public enum ApplicationRecordType : byte
{
    Game = 0,
    Update = 1,
    Dlc = 2,
    SystemApplet = 3,
}

/// <summary>
/// ns:am2 — 应用管理服务 (完整管理端口)
/// nn::ns::detail::IApplicationManagerInterface
/// 提供完整的应用记录管理、启动、查询功能
/// 命令表基于 SwitchBrew NS_Services 页面
/// </summary>
public sealed class NsAm2Service : IIpcService
{
    public string PortName => "ns:am2";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly NsAppManagerState _state;

    public NsAm2Service(NsAppManagerState state)
    {
        _state = state;
        _commandTable = BuildCommandTable();
    }

    private Dictionary<uint, ServiceCommand> BuildCommandTable()
    {
        return new Dictionary<uint, ServiceCommand>
        {
            [0]  = ListApplicationRecord,              // 列出应用记录
            [1]  = GenerateApplicationRecordCount,     // 获取应用记录总数
            [2]  = GetApplicationRecordUpdateSystemEvent, // 获取记录更新事件 (stub)
            [4]  = DeleteApplicationEntity,             // 删除应用实体
            [5]  = DeleteApplicationCompletely,         // 完全删除应用
            [6]  = IsAnyApplicationEntityRedundant,     // 是否有冗余应用实体 (stub)
            [11] = CalculateApplicationOccupiedSize,    // 计算应用占用大小
            [16] = PushApplicationRecord,               // 推送应用记录 (stub)
            [17] = ListApplicationRecordContentMeta,    // 列出内容元数据 (stub)
            [19] = LaunchApplicationOld,                // 启动应用 (旧版, stub)
            [21] = GetApplicationContentPath,           // 获取应用内容路径 (stub)
            [23] = GetApplicationDesiredLanguage,       // 获取应用期望语言
            [26] = IsApplicationPlayed,                 // 是否已游玩
            [30] = IsApplicationPlayable,               // 是否可游玩
            [40] = GetApplicationDisplayInfo,           // 获取应用显示信息
            [42] = GetApplicationDownloadTaskStatus,    // 获取下载任务状态 (stub)
            [50] = GetApplicationMetaData,              // 获取应用元数据
            [60] = GetAppControlData,                   // 获取应用控制数据 (NACP)
            [65] = CheckAppLaunchVersion,               // 检查启动版本 (stub)
        };
    }

    /// <summary>命令 0: ListApplicationRecord — 列出应用记录</summary>
    private ResultCode ListApplicationRecord(IpcRequest request, ref IpcResponse response)
    {
        // ApplicationRecord: ProgramId(8) + Type(u8) + padding(7) = 16 bytes per record
        foreach (var record in _state.Records)
        {
            response.Data.AddRange(BitConverter.GetBytes(record.ProgramId));
            response.Data.Add((byte)record.Type);
            response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
            response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); response.Data.Add(0);
        }
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: ListApplicationRecord → {_state.Records.Count} records");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GenerateApplicationRecordCount — 获取应用记录总数</summary>
    private ResultCode GenerateApplicationRecordCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.Records.Count));
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: GenerateApplicationRecordCount → {_state.Records.Count}");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetApplicationRecordUpdateSystemEvent — 获取记录更新事件 (stub)</summary>
    private ResultCode GetApplicationRecordUpdateSystemEvent(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0901);
        response.Data.AddRange(BitConverter.GetBytes(handle)); // KEvent handle
        Logger.Debug(nameof(NsAm2Service), "ns:am2: GetApplicationRecordUpdateSystemEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 4: DeleteApplicationEntity — 删除应用实体</summary>
    private ResultCode DeleteApplicationEntity(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        bool removed = _state.RemoveRecord(programId);
        if (!removed) return ResultCode.NsResult(6); // Not found
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: DeleteApplicationEntity(id=0x{programId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 5: DeleteApplicationCompletely — 完全删除应用</summary>
    private ResultCode DeleteApplicationCompletely(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        _state.RemoveRecord(programId);
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: DeleteApplicationCompletely(id=0x{programId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 6: IsAnyApplicationEntityRedundant — 是否有冗余应用实体 (stub)</summary>
    private ResultCode IsAnyApplicationEntityRedundant(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // false
        Logger.Debug(nameof(NsAm2Service), "ns:am2: IsAnyApplicationEntityRedundant → false (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 11: CalculateApplicationOccupiedSize — 计算应用占用大小</summary>
    private ResultCode CalculateApplicationOccupiedSize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        var record = _state.FindByProgramId(programId);
        ulong size = record?.InstalledSize ?? 0;
        response.Data.AddRange(BitConverter.GetBytes(size));
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: CalculateApplicationOccupiedSize(id=0x{programId:X16}) → {size}");
        return ResultCode.Success;
    }

    /// <summary>命令 16: PushApplicationRecord — 推送应用记录 (stub)</summary>
    private ResultCode PushApplicationRecord(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsAm2Service), "ns:am2: PushApplicationRecord (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 17: ListApplicationRecordContentMeta — 列出内容元数据 (stub)</summary>
    private ResultCode ListApplicationRecordContentMeta(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(NsAm2Service), "ns:am2: ListApplicationRecordContentMeta → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 19: LaunchApplicationOld — 启动应用 (旧版, stub)</summary>
    private ResultCode LaunchApplicationOld(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: LaunchApplicationOld(id=0x{programId:X16}) (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 21: GetApplicationContentPath — 获取应用内容路径 (stub)</summary>
    private ResultCode GetApplicationContentPath(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsAm2Service), "ns:am2: GetApplicationContentPath (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 23: GetApplicationDesiredLanguage — 获取应用期望语言</summary>
    private ResultCode GetApplicationDesiredLanguage(IpcRequest request, ref IpcResponse response)
    {
        // 返回语言码: 0=Japanese, 1=English, 2=French, etc. — 默认英语
        response.Data.AddRange(BitConverter.GetBytes(1U)); // English
        Logger.Debug(nameof(NsAm2Service), "ns:am2: GetApplicationDesiredLanguage → English");
        return ResultCode.Success;
    }

    /// <summary>命令 26: IsApplicationPlayed — 是否已游玩</summary>
    private ResultCode IsApplicationPlayed(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        var record = _state.FindByProgramId(programId);
        response.Data.AddRange(BitConverter.GetBytes(record != null ? 1U : 0U));
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: IsApplicationPlayed(id=0x{programId:X16}) → {record != null}");
        return ResultCode.Success;
    }

    /// <summary>命令 30: IsApplicationPlayable — 是否可游玩</summary>
    private ResultCode IsApplicationPlayable(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        // 模拟器中始终可游玩
        response.Data.AddRange(BitConverter.GetBytes(1U)); // playable
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: IsApplicationPlayable(id=0x{programId:X16}) → true");
        return ResultCode.Success;
    }

    /// <summary>命令 40: GetApplicationDisplayInfo — 获取应用显示信息</summary>
    private ResultCode GetApplicationDisplayInfo(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        var record = _state.FindByProgramId(programId);
        if (record == null) return ResultCode.NsResult(6); // Not found

        // ApplicationDisplayInfo: ProgramId(8) + Name(0x200 bytes, UTF-16LE, cached) = 0x208 bytes
        response.Data.AddRange(BitConverter.GetBytes(record.ProgramId));
        response.Data.AddRange(_state.GetDisplayNameBuffer(programId));
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: GetApplicationDisplayInfo(id=0x{programId:X16}) → '{record.Name}'");
        return ResultCode.Success;
    }

    /// <summary>命令 42: GetApplicationDownloadTaskStatus — 获取下载任务状态 (stub)</summary>
    private ResultCode GetApplicationDownloadTaskStatus(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // no download tasks
        Logger.Debug(nameof(NsAm2Service), "ns:am2: GetApplicationDownloadTaskStatus → none (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 50: GetApplicationMetaData — 获取应用元数据</summary>
    private ResultCode GetApplicationMetaData(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        var record = _state.FindByProgramId(programId);
        if (record == null) return ResultCode.NsResult(6);

        // ApplicationMetaData: ProgramId(8) + Version(4) + Type(u8) + padding(3) = 16 bytes
        response.Data.AddRange(BitConverter.GetBytes(record.ProgramId));
        response.Data.AddRange(BitConverter.GetBytes(record.Version));
        response.Data.Add((byte)record.Type);
        response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: GetApplicationMetaData(id=0x{programId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 60: GetAppControlData — 获取应用控制数据 (NACP)</summary>
    private ResultCode GetAppControlData(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.NsResult(2);
        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        var record = _state.FindByProgramId(programId);
        if (record == null) return ResultCode.NsResult(6);

        // NACP 控制数据 (0x4000 bytes, cached per ProgramId)
        response.Data.AddRange(BitConverter.GetBytes(0x4000U)); // size
        response.Data.AddRange(_state.GetNacpBuffer(programId));
        Logger.Debug(nameof(NsAm2Service), $"ns:am2: GetAppControlData(id=0x{programId:X16}) → 0x4000 bytes");
        return ResultCode.Success;
    }

    /// <summary>命令 65: CheckAppLaunchVersion — 检查启动版本 (stub)</summary>
    private ResultCode CheckAppLaunchVersion(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsAm2Service), "ns:am2: CheckAppLaunchVersion (stub)");
        return ResultCode.Success;
    }

    internal NsAppManagerState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// ns:am — 应用代理服务 (应用端口)
/// nn::ns::detail::IApplicationProxyInterface
/// Guest 应用通过此端口获取 IApplicationManagerInterface
/// </summary>
public sealed class NsAmService : IIpcService
{
    public string PortName => "ns:am";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    // NsAppManagerState reserved for future handle→IApplicationManagerInterface mapping
    private readonly NsAppManagerState _state;

    public NsAmService(NsAppManagerState state)
    {
        _state = state; // reserved: will be used when handle→service dispatch is implemented
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetUserService,               // 获取 IApplicationManagerInterface
            [1] = GetAdminService,              // 获取 IApplicationManagerInterface (admin, stub)
        };
    }

    /// <summary>命令 0: GetUserService — 获取 IApplicationManagerInterface</summary>
    private ResultCode GetUserService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0900);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsAmService), "ns:am: GetUserService → IApplicationManagerInterface handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetAdminService — 获取 IApplicationManagerInterface (admin, stub)</summary>
    private ResultCode GetAdminService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0901);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsAmService), "ns:am: GetAdminService → IApplicationManagerInterface handle (admin)");
        return ResultCode.Success;
    }

    internal NsAppManagerState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// ns:ae — 应用代理服务 (全部权限端口)
/// </summary>
public sealed class NsAeService : IIpcService
{
    public string PortName => "ns:ae";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    // NsAppManagerState reserved for future handle→IApplicationManagerInterface mapping
    private readonly NsAppManagerState _state;

    public NsAeService(NsAppManagerState state)
    {
        _state = state; // reserved: will be used when handle→service dispatch is implemented
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetApplicationManagerInterface,
            [1] = GetSystemUpdateInterface,      // 获取 ISystemUpdateInterface (stub)
        };
    }

    /// <summary>命令 0: GetApplicationManagerInterface — 获取 IApplicationManagerInterface</summary>
    private ResultCode GetApplicationManagerInterface(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0902);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsAeService), "ns:ae: GetApplicationManagerInterface → handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetSystemUpdateInterface — 获取 ISystemUpdateInterface (stub)</summary>
    private ResultCode GetSystemUpdateInterface(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0910);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsAeService), "ns:ae: GetSystemUpdateInterface → handle (stub)");
        return ResultCode.Success;
    }

    internal NsAppManagerState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// ns:su — 系统更新服务
/// nn::ns::detail::ISystemUpdateInterface
/// </summary>
public sealed class NsSuService : IIpcService
{
    public string PortName => "ns:su";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public NsSuService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = GetBackgroundNetworkUpdateState,     // 获取后台网络更新状态
            [1]  = OpenSystemUpdateControl,             // 打开系统更新控制 (stub)
            [2]  = NotifyExFatDriverRequired,           // 通知 ExFAT 驱动需求 (stub)
            [10] = RequestBackgroundNetworkUpdate,      // 请求后台网络更新 (stub)
            [20] = GetSystemUpdateNotificationEvent,    // 获取系统更新通知事件 (stub)
            [40] = PrepareShutdown,                     // 准备关机 (stub)
            [50] = DestroySystemUpdateTask,             // 销毁系统更新任务 (stub)
        };
    }

    /// <summary>命令 0: GetBackgroundNetworkUpdateState — 获取后台网络更新状态</summary>
    private ResultCode GetBackgroundNetworkUpdateState(IpcRequest request, ref IpcResponse response)
    {
        // BackgroundNetworkUpdateState: u8 state (0=None, 1=Downloading, 2=ReadyToInstall)
        response.Data.Add(0); // None
        response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
        Logger.Debug(nameof(NsSuService), "ns:su: GetBackgroundNetworkUpdateState → None");
        return ResultCode.Success;
    }

    /// <summary>命令 1: OpenSystemUpdateControl — 打开系统更新控制 (stub)</summary>
    private ResultCode OpenSystemUpdateControl(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0920);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsSuService), "ns:su: OpenSystemUpdateControl → handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: NotifyExFatDriverRequired — 通知 ExFAT 驱动需求 (stub)</summary>
    private ResultCode NotifyExFatDriverRequired(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsSuService), "ns:su: NotifyExFatDriverRequired (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10: RequestBackgroundNetworkUpdate — 请求后台网络更新 (stub)</summary>
    private ResultCode RequestBackgroundNetworkUpdate(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsSuService), "ns:su: RequestBackgroundNetworkUpdate (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 20: GetSystemUpdateNotificationEvent — 获取系统更新通知事件 (stub)</summary>
    private ResultCode GetSystemUpdateNotificationEvent(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0921);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsSuService), "ns:su: GetSystemUpdateNotificationEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 40: PrepareShutdown — 准备关机 (stub)</summary>
    private ResultCode PrepareShutdown(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsSuService), "ns:su: PrepareShutdown (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 50: DestroySystemUpdateTask — 销毁系统更新任务 (stub)</summary>
    private ResultCode DestroySystemUpdateTask(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NsSuService), "ns:su: DestroySystemUpdateTask (stub)");
        return ResultCode.Success;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// ns:dev — 开发者服务端口 (stub)
/// </summary>
public sealed class NsDevService : IIpcService
{
    public string PortName => "ns:dev";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public NsDevService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetService, // stub — 返回虚拟句柄
        };
    }

    /// <summary>命令 0: GetService — 获取开发者服务 (stub)</summary>
    private ResultCode GetService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0930);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NsDevService), "ns:dev: GetService → handle (stub)");
        return ResultCode.Success;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
