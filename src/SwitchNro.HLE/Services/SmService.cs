using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// sm: 服务管理器 (Service Manager)
/// 核心必选服务 - NRO 启动后第一个连接的服务
/// 负责服务发现，其他服务通过 sm: 注册和查找
/// </summary>
public sealed class SmService : IIpcService
{
    private readonly IpcServiceManager _serviceManager;

    public string PortName => "sm:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public SmService(IpcServiceManager serviceManager)
    {
        _serviceManager = serviceManager;

        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = Initialize,          // 初始化会话
            [1] = GetService,          // 获取服务句柄
            [2] = RegisterService,     // 注册服务（通常不由 guest 调用）
        };
    }

    /// <summary>命令 0: Initialize — 初始化与 sm: 的会话</summary>
    private ResultCode Initialize(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SmService), "sm: 会话初始化");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetService — 通过服务名称获取服务句柄</summary>
    private ResultCode GetService(IpcRequest request, ref IpcResponse response)
    {
        // 从请求数据中读取服务名称（8 字节，null-terminated）
        var serviceName = System.Text.Encoding.ASCII.GetString(request.Data).TrimEnd('\0');
        Logger.Info(nameof(SmService), $"请求服务: {serviceName}");

        var service = _serviceManager.GetService(serviceName);
        if (service == null)
        {
            Logger.Warning(nameof(SmService), $"服务未找到: {serviceName}");
            return new ResultCode(10, 0x640); // Service not registered
        }

        // 返回服务会话句柄
        response.CopyHandles.Add(GenerateHandle(serviceName));
        return ResultCode.Success;
    }

    /// <summary>命令 2: RegisterService — 注册新服务</summary>
    private ResultCode RegisterService(IpcRequest request, ref IpcResponse response)
    {
        var serviceName = System.Text.Encoding.ASCII.GetString(request.Data).TrimEnd('\0');
        Logger.Info(nameof(SmService), $"注册服务: {serviceName}");
        // 通常 NRO 不会注册服务，但保留接口
        return ResultCode.Success;
    }

    private static int _nextHandle = 0x100;
    private static int GenerateHandle(string serviceName) =>
        Interlocked.Increment(ref _nextHandle);

    public void Dispose() { }
}
