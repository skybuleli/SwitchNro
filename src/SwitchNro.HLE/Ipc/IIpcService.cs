using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Horizon;

namespace SwitchNro.HLE.Ipc;

/// <summary>
/// IPC 服务接口
/// 所有 HLE 系统服务实现此接口
/// </summary>
public interface IIpcService : IDisposable
{
    /// <summary>服务端口名称（如 "sm:", "fs:"）</summary>
    string PortName { get; }

    /// <summary>服务的命令 ID → 方法映射表</summary>
    IReadOnlyDictionary<uint, ServiceCommand> CommandTable { get; }
}

/// <summary>服务命令委托</summary>
public delegate ResultCode ServiceCommand(IpcRequest request, ref IpcResponse response);

/// <summary>
/// IPC 服务管理器
/// 注册和分发 IPC 服务
/// </summary>
public sealed class IpcServiceManager
{
    private readonly Dictionary<string, IIpcService> _services = new();

    /// <summary>
    /// 当前进程的句柄表（由外部在进程创建后设置）
    /// SmService.GetService 通过此句柄表创建真实的 KClientSession 内核对象句柄
    /// </summary>
    public HandleTable? HandleTable { get; set; }

    /// <summary>注册服务实现</summary>
    public void RegisterService<T>(T implementation) where T : IIpcService
    {
        _services[implementation.PortName] = implementation;
    }

    /// <summary>通过端口名称查找服务</summary>
    public IIpcService? GetService(string portName)
    {
        return _services.TryGetValue(portName, out var service) ? service : null;
    }

    /// <summary>处理 IPC 请求</summary>
    public ResultCode HandleRequest(string portName, IpcRequest request, ref IpcResponse response)
    {
        var service = GetService(portName);
        if (service == null)
        {
            return ResultCode.SfResult(1); // Service not found
        }

        if (request.Header.CommandType == IpcCommandType.Close ||
            request.Header.CommandType == IpcCommandType.TipcClose)
        {
            return ResultCode.Success; // 关闭会话
        }

        if (service.CommandTable.TryGetValue(request.CommandId, out var handler))
        {
            return handler(request, ref response);
        }

        return ResultCode.SfResult(2); // Command not found
    }

    /// <summary>获取所有已注册服务</summary>
    public IReadOnlyCollection<IIpcService> GetAllServices() => _services.Values;
}
