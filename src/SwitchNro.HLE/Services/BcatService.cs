using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 BCAT 服务状态
/// </summary>
public sealed class BcatState
{
    /// <summary>是否已同步</summary>
    private bool _synced;

    /// <summary>最后同步时间</summary>
    private long _lastSyncTimestamp;

    /// <summary>传递缓存目录数量</summary>
    private int _directoryCount;

    /// <summary>是否已同步</summary>
    public bool Synced
    {
        get => _synced;
        set => _synced = value;
    }

    /// <summary>最后同步时间</summary>
    public long LastSyncTimestamp
    {
        get => _lastSyncTimestamp;
        set => _lastSyncTimestamp = value;
    }

    /// <summary>传递缓存目录数量</summary>
    public int DirectoryCount
    {
        get => _directoryCount;
        set => _directoryCount = value;
    }
}

/// <summary>
/// BCAT 服务创建器基类 — bcat:a/m/u/s 共享的命令处理逻辑
/// nn::bcat::detail::ipc::IServiceCreator
/// 四个端口命令表完全相同，仅端口名和权限不同
/// 命令表基于 SwitchBrew BCAT_services 页面
/// </summary>
public abstract class BcatServiceCreatorBase : IIpcService
{
    public abstract string PortName { get; }

    /// <summary>每端口唯一的句柄偏移量 (0~3)</summary>
    protected abstract int HandleOffset { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly BcatState _state;

    protected BcatServiceCreatorBase(BcatState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateBcatService,                              // 创建 IBcatService
            [1] = CreateDeliveryCacheStorageService,              // 创建 IDeliveryCacheStorageService
            [2] = CreateDeliveryCacheStorageServiceWithAppId,     // 创建 IDeliveryCacheStorageService (含 ApplicationId)
            [3] = CreateDeliveryCacheProgressService,            // 创建 IDeliveryCacheProgressService
            [4] = CreateDeliveryCacheProgressServiceWithAppId,   // 创建 IDeliveryCacheProgressService (含 ApplicationId)
        };
    }

    /// <summary>命令 0: CreateBcatService — 创建 IBcatService</summary>
    private ResultCode CreateBcatService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong processId = BitConverter.ToUInt64(request.Data, 0);
        int handle = unchecked((int)(0xFFFF0A00 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: CreateBcatService(pid=0x{processId:X16}) → IBcatService handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: CreateDeliveryCacheStorageService — 创建 IDeliveryCacheStorageService</summary>
    private ResultCode CreateDeliveryCacheStorageService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong processId = BitConverter.ToUInt64(request.Data, 0);
        int handle = unchecked((int)(0xFFFF0A10 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: CreateDeliveryCacheStorageService(pid=0x{processId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 2: CreateDeliveryCacheStorageServiceWithApplicationId — 创建 IDeliveryCacheStorageService (含 ApplicationId)</summary>
    private ResultCode CreateDeliveryCacheStorageServiceWithAppId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong appId = BitConverter.ToUInt64(request.Data, 0);
        int handle = unchecked((int)(0xFFFF0A20 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: CreateDeliveryCacheStorageServiceWithAppId(appId=0x{appId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 3: CreateDeliveryCacheProgressService — 创建 IDeliveryCacheProgressService</summary>
    private ResultCode CreateDeliveryCacheProgressService(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong processId = BitConverter.ToUInt64(request.Data, 0);
        int handle = unchecked((int)(0xFFFF0A30 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: CreateDeliveryCacheProgressService(pid=0x{processId:X16})");
        return ResultCode.Success;
    }

    /// <summary>命令 4: CreateDeliveryCacheProgressServiceWithApplicationId — 创建 IDeliveryCacheProgressService (含 ApplicationId)</summary>
    private ResultCode CreateDeliveryCacheProgressServiceWithAppId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong appId = BitConverter.ToUInt64(request.Data, 0);
        int handle = unchecked((int)(0xFFFF0A40 + HandleOffset));
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(PortName, $"{PortName}: CreateDeliveryCacheProgressServiceWithAppId(appId=0x{appId:X16})");
        return ResultCode.Success;
    }

    internal BcatState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>bcat:a — BCAT 服务 (管理员端口)</summary>
public sealed class BcatAService : BcatServiceCreatorBase
{
    public override string PortName => "bcat:a";
    protected override int HandleOffset => 0;
    public BcatAService(BcatState state) : base(state) { }
}

/// <summary>bcat:m — BCAT 服务 (管理端口)</summary>
public sealed class BcatMService : BcatServiceCreatorBase
{
    public override string PortName => "bcat:m";
    protected override int HandleOffset => 1;
    public BcatMService(BcatState state) : base(state) { }
}

/// <summary>bcat:u — BCAT 服务 (用户端口)</summary>
public sealed class BcatUService : BcatServiceCreatorBase
{
    public override string PortName => "bcat:u";
    protected override int HandleOffset => 2;
    public BcatUService(BcatState state) : base(state) { }
}

/// <summary>bcat:s — BCAT 服务 (系统端口)</summary>
public sealed class BcatSService : BcatServiceCreatorBase
{
    public override string PortName => "bcat:s";
    protected override int HandleOffset => 3;
    public BcatSService(BcatState state) : base(state) { }
}

/// <summary>
/// IBcatService — BCAT 服务接口
/// nn::bcat::detail::ipc::IBcatService
/// 通过 bcat:a/m/u/s 的 CreateBcatService 获取
/// 提供传递缓存同步、请求管理等功能
/// 命令表基于 SwitchBrew BCAT_services 页面
/// </summary>
public sealed class BcatService : IIpcService
{
    public string PortName => "bcat:svc"; // 内部虚拟端口名 — 通过 CreateBcatService 获取，不注册为命名端口

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly BcatState _state;

    public BcatService(BcatState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [10100] = RequestSyncDeliveryCache,                    // 请求同步传递缓存
            [10101] = RequestSyncDeliveryCacheWithDirectoryName,  // 请求同步传递缓存 (含目录名)
            [10200] = CancelSyncDeliveryCacheRequest,              // 取消同步请求 (5.0.0+)
            [20100] = RequestSyncDeliveryCacheWithApplicationId,  // 请求同步传递缓存 (含 AppId)
            [20101] = RequestSyncDeliveryCacheWithAppIdAndDirName,// 请求同步 (含 AppId + 目录名)
            [20300] = GetDeliveryCacheStorageUpdateNotifier,      // 获取存储更新通知器
            [20301] = RequestSuspendDeliveryTask,                  // 请求暂停传递任务
            [30100] = SetPassphrase,                               // 设置密码短语 (stub)
            [90100] = GetDeliveryTaskList,                         // 获取传递任务列表 (stub)
            [90200] = GetDeliveryList,                             // 获取传递列表 (stub)
        };
    }

    /// <summary>命令 10100: RequestSyncDeliveryCache — 请求同步传递缓存</summary>
    private ResultCode RequestSyncDeliveryCache(IpcRequest request, ref IpcResponse response)
    {
        _state.Synced = true;
        _state.LastSyncTimestamp = DateTime.UtcNow.ToBinary();
        int handle = unchecked((int)0xFFFF0A50); // IDeliveryCacheProgressService handle
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(BcatService), "bcat:svc: RequestSyncDeliveryCache → synced (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10101: RequestSyncDeliveryCacheWithDirectoryName — 请求同步传递缓存 (含目录名)</summary>
    private ResultCode RequestSyncDeliveryCacheWithDirectoryName(IpcRequest request, ref IpcResponse response)
    {
        _state.Synced = true;
        Logger.Debug(nameof(BcatService), "bcat:svc: RequestSyncDeliveryCacheWithDirectoryName (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10200: CancelSyncDeliveryCacheRequest — 取消同步请求</summary>
    private ResultCode CancelSyncDeliveryCacheRequest(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(BcatService), "bcat:svc: CancelSyncDeliveryCacheRequest (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 20100: RequestSyncDeliveryCacheWithApplicationId — 请求同步 (含 AppId)</summary>
    private ResultCode RequestSyncDeliveryCacheWithApplicationId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong appId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(BcatService), $"bcat:svc: RequestSyncDeliveryCacheWithApplicationId(appId=0x{appId:X16}) (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 20101: RequestSyncDeliveryCacheWithAppIdAndDirName — 请求同步 (含 AppId + 目录名)</summary>
    private ResultCode RequestSyncDeliveryCacheWithAppIdAndDirName(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(BcatService), "bcat:svc: RequestSyncDeliveryCacheWithAppIdAndDirName (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 20300: GetDeliveryCacheStorageUpdateNotifier — 获取存储更新通知器</summary>
    private ResultCode GetDeliveryCacheStorageUpdateNotifier(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0A60); // INotifierService handle
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(BcatService), "bcat:svc: GetDeliveryCacheStorageUpdateNotifier → INotifierService handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 20301: RequestSuspendDeliveryTask — 请求暂停传递任务</summary>
    private ResultCode RequestSuspendDeliveryTask(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0A61); // IDeliveryTaskSuspensionService handle
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(BcatService), "bcat:svc: RequestSuspendDeliveryTask (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 30100: SetPassphrase — 设置密码短语 (stub)</summary>
    private ResultCode SetPassphrase(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.BcatResult(2);
        ulong appId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(BcatService), $"bcat:svc: SetPassphrase(appId=0x{appId:X16}) (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 90100: GetDeliveryTaskList — 获取传递任务列表 (stub)</summary>
    private ResultCode GetDeliveryTaskList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(BcatService), "bcat:svc: GetDeliveryTaskList → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 90200: GetDeliveryList — 获取传递列表 (stub)</summary>
    private ResultCode GetDeliveryList(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        response.Data.AddRange(BitConverter.GetBytes(0U)); // totalSize = 0
        Logger.Debug(nameof(BcatService), "bcat:svc: GetDeliveryList → 0 (stub)");
        return ResultCode.Success;
    }

    internal BcatState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// IDeliveryCacheStorageService — 传递缓存存储服务
/// nn::bcat::detail::ipc::IDeliveryCacheStorageService
/// </summary>
public sealed class DeliveryCacheStorageService : IIpcService
{
    public string PortName => "bcat:dcss"; // 内部虚拟端口名

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public DeliveryCacheStorageService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = CreateFileService,                    // 创建 IDeliveryCacheFileService
            [1]  = CreateDirectoryService,               // 创建 IDeliveryCacheDirectoryService
            [10] = EnumerateDeliveryCacheDirectory,      // 枚举传递缓存目录 (stub)
        };
    }

    /// <summary>命令 0: CreateFileService — 创建 IDeliveryCacheFileService</summary>
    private ResultCode CreateFileService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0A70);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(DeliveryCacheStorageService), "bcat:dcss: CreateFileService → handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 1: CreateDirectoryService — 创建 IDeliveryCacheDirectoryService</summary>
    private ResultCode CreateDirectoryService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0A71);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(DeliveryCacheStorageService), "bcat:dcss: CreateDirectoryService → handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10: EnumerateDeliveryCacheDirectory — 枚举传递缓存目录 (stub)</summary>
    private ResultCode EnumerateDeliveryCacheDirectory(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // count = 0
        Logger.Debug(nameof(DeliveryCacheStorageService), "bcat:dcss: EnumerateDeliveryCacheDirectory → 0 (stub)");
        return ResultCode.Success;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// IDeliveryCacheProgressService — 传递缓存进度服务
/// nn::bcat::detail::ipc::IDeliveryCacheProgressService
/// </summary>
public sealed class DeliveryCacheProgressService : IIpcService
{
    public string PortName => "bcat:dcps"; // 内部虚拟端口名

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public DeliveryCacheProgressService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetEvent,    // 获取进度完成事件
            [1] = GetImpl,     // 获取进度实现 (stub)
        };
    }

    /// <summary>命令 0: GetEvent — 获取进度完成事件</summary>
    private ResultCode GetEvent(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0A80);
        response.Data.AddRange(BitConverter.GetBytes(handle)); // KEvent handle
        Logger.Debug(nameof(DeliveryCacheProgressService), "bcat:dcps: GetEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetImpl — 获取进度实现 (stub)</summary>
    private ResultCode GetImpl(IpcRequest request, ref IpcResponse response)
    {
        // DeliveryCacheProgress: u8 status(0=NotStarted/1=InProgress/2=Done/3=Error) + padding
        response.Data.Add(2); // Done
        response.Data.Add(0); response.Data.Add(0); response.Data.Add(0); // padding
        Logger.Debug(nameof(DeliveryCacheProgressService), "bcat:dcps: GetImpl → Done (stub)");
        return ResultCode.Success;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
