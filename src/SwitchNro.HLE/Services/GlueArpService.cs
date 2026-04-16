using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// arp:r — ARP 读取服务 (nn::arp::detail::IReader)
/// 提供应用启动属性、控制属性、实例 ID 等只读查询
/// Homebrew 通过此服务获取应用元数据
/// </summary>
public sealed class ArpRService : IIpcService
{
    public string PortName => "arp:r";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>共享的应用注册表引用</summary>
    private readonly ArpRegistry _registry;

    public ArpRService(ArpRegistry registry)
    {
        _registry = registry;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]    = GetApplicationLaunchProperty,             // 获取应用启动属性
            [1]    = GetApplicationControlProperty,            // 获取应用控制属性 (NACP)
            [2]    = GetApplicationProcessProperty,            // 获取应用进程属性
            [3]    = GetApplicationInstanceId,                // 获取应用实例 ID
            [4]    = GetApplicationInstanceUnregistrationNotifier, // 获取注销通知器
            [5]    = ListApplicationInstanceId,               // 列出所有应用实例 ID
        };
    }

    /// <summary>命令 0: GetApplicationLaunchProperty — 获取应用启动属性</summary>
    private ResultCode GetApplicationLaunchProperty(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.ArpResult(2); // Invalid size

        ulong processId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(ArpRService), $"arp:r: GetApplicationLaunchProperty(PID={processId})");

        var entry = _registry.FindByProcessId(processId);
        if (entry == null)
            return ResultCode.ArpResult(4); // Not registered

        // ApplicationLaunchProperty: 0x10 bytes
        // ProgramId(8) + LaunchFlags(4) + LaunchMode(1) + padding(3) → 简化为 ProgramId(8) + flags(4) + mode(4)
        response.Data.AddRange(BitConverter.GetBytes(entry.ProgramId));
        response.Data.AddRange(BitConverter.GetBytes(entry.LaunchFlags));
        response.Data.AddRange(BitConverter.GetBytes((uint)entry.LaunchMode));
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetApplicationControlProperty — 获取应用控制属性 (NACP)</summary>
    private ResultCode GetApplicationControlProperty(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.ArpResult(2);

        ulong processId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(ArpRService), $"arp:r: GetApplicationControlProperty(PID={processId})");

        var entry = _registry.FindByProcessId(processId);
        if (entry == null)
            return ResultCode.ArpResult(4);

        // NACP 数据 (0x4000 bytes 标准大小，此处返回简化版)
        if (entry.ControlProperty != null)
        {
            response.Data.AddRange(entry.ControlProperty);
        }
        else
        {
            // 返回最小 NACP: 应用名称占位
            var nameBytes = Encoding.UTF8.GetBytes("Homebrew\0");
            var nacp = new byte[0x300]; // 简化尺寸
            nameBytes.CopyTo(nacp, 0);
            response.Data.AddRange(nacp);
        }

        return ResultCode.Success;
    }

    /// <summary>命令 2: GetApplicationProcessProperty — 获取应用进程属性</summary>
    private ResultCode GetApplicationProcessProperty(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.ArpResult(2);

        ulong instanceId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(ArpRService), $"arp:r: GetApplicationProcessProperty(InstanceId={instanceId})");

        var entry = _registry.FindByInstanceId(instanceId);
        if (entry == null)
            return ResultCode.ArpResult(4);

        // ApplicationProcessProperty: ProcessId(8) + ProgramId(8) + flags...
        response.Data.AddRange(BitConverter.GetBytes(entry.ProcessId));
        response.Data.AddRange(BitConverter.GetBytes(entry.ProgramId));
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetApplicationInstanceId — 获取应用实例 ID</summary>
    private ResultCode GetApplicationInstanceId(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.ArpResult(2);

        ulong processId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Debug(nameof(ArpRService), $"arp:r: GetApplicationInstanceId(PID={processId})");

        var entry = _registry.FindByProcessId(processId);
        if (entry == null)
            return ResultCode.ArpResult(4);

        response.Data.AddRange(BitConverter.GetBytes(entry.InstanceId));
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetApplicationInstanceUnregistrationNotifier — 获取注销通知器</summary>
    private ResultCode GetApplicationInstanceUnregistrationNotifier(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(ArpRService), "arp:r: GetApplicationInstanceUnregistrationNotifier");
        response.Data.AddRange(BitConverter.GetBytes(0xFFFF0300)); // 虚拟事件句柄
        return ResultCode.Success;
    }

    /// <summary>命令 5: ListApplicationInstanceId — 列出所有应用实例 ID</summary>
    private ResultCode ListApplicationInstanceId(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(ArpRService), "arp:r: ListApplicationInstanceId");

        var instances = _registry.GetAllInstances();
        foreach (var instance in instances)
            response.Data.AddRange(BitConverter.GetBytes(instance.InstanceId));

        // 末尾附加计数
        response.Data.AddRange(BitConverter.GetBytes(instances.Count));
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// arp:w — ARP 写入服务 (nn::arp::detail::IWriter)
/// 提供应用注册/注销和属性写入接口
/// 仅限系统模块使用
/// </summary>
public sealed class ArpWService : IIpcService
{
    public string PortName => "arp:w";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>共享的应用注册表引用</summary>
    private readonly ArpRegistry _registry;

    public ArpWService(ArpRegistry registry)
    {
        _registry = registry;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = AcquireRegistrar,                      // 获取注册器
            [1] = UnregisterApplicationInstance,         // 注销应用实例
        };
    }

    /// <summary>命令 0: AcquireRegistrar — 获取 IRegistrar（用于注册新应用）</summary>
    private ResultCode AcquireRegistrar(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(ArpWService), "arp:w: AcquireRegistrar");
        // 返回虚拟注册器句柄
        response.Data.AddRange(BitConverter.GetBytes(0xFFFF0400));
        return ResultCode.Success;
    }

    /// <summary>命令 1: UnregisterApplicationInstance — 注销应用实例</summary>
    private ResultCode UnregisterApplicationInstance(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.ArpResult(2);

        ulong instanceId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Info(nameof(ArpWService), $"arp:w: UnregisterApplicationInstance(InstanceId={instanceId})");

        if (!_registry.Unregister(instanceId))
        {
            Logger.Warning(nameof(ArpWService), $"arp:w: 未找到 InstanceId={instanceId}");
            return ResultCode.ArpResult(4); // Not registered
        }

        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// ARP 应用注册表 — 共享于 arp:r 和 arp:w 之间
/// 存储应用实例的元数据映射
/// </summary>
public sealed class ArpRegistry
{
    private readonly List<ArpApplicationEntry> _entries = new();
    private ulong _nextInstanceId = 0x100;

    /// <summary>注册新应用实例</summary>
    public ArpApplicationEntry Register(ulong processId, ulong programId, uint launchFlags = 0, ArpLaunchMode launchMode = ArpLaunchMode.Application)
    {
        var entry = new ArpApplicationEntry
        {
            InstanceId = _nextInstanceId++,
            ProcessId = processId,
            ProgramId = programId,
            LaunchFlags = launchFlags,
            LaunchMode = launchMode,
        };
        _entries.Add(entry);
        return entry;
    }

    /// <summary>注销应用实例</summary>
    public bool Unregister(ulong instanceId)
    {
        int index = _entries.FindIndex(e => e.InstanceId == instanceId);
        if (index < 0) return false;
        _entries.RemoveAt(index);
        return true;
    }

    /// <summary>通过 ProcessId 查找</summary>
    public ArpApplicationEntry? FindByProcessId(ulong processId) =>
        _entries.FirstOrDefault(e => e.ProcessId == processId);

    /// <summary>通过 InstanceId 查找</summary>
    public ArpApplicationEntry? FindByInstanceId(ulong instanceId) =>
        _entries.FirstOrDefault(e => e.InstanceId == instanceId);

    /// <summary>获取所有实例</summary>
    public IReadOnlyList<ArpApplicationEntry> GetAllInstances() => _entries;
}

/// <summary>ARP 应用条目</summary>
public sealed class ArpApplicationEntry
{
    public ulong InstanceId { get; init; }
    public ulong ProcessId { get; init; }
    public ulong ProgramId { get; init; }
    public uint LaunchFlags { get; init; }
    public ArpLaunchMode LaunchMode { get; init; }
    public byte[]? ControlProperty { get; set; }
}

/// <summary>ARP 启动模式</summary>
public enum ArpLaunchMode : uint
{
    Application = 0,
    Application2 = 1,
    GameCard = 2,
    Download = 3,
}
