using System;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.Horizon;
using SwitchNro.Memory;

namespace SwitchNro.HLE.Ipc;

/// <summary>
/// IPC 桥接器
/// 将 guest NRO 的 SVC 系统调用桥接到 HLE 服务层
/// 处理 ConnectToNamedPort、SendSyncRequest/WithUserBuffer、
/// ReplyAndReceive/WithUserBuffer
/// </summary>
public sealed class IpcBridge
{
    private readonly IpcServiceManager _serviceManager;
    private readonly VirtualMemoryManager _memory;

    /// <summary>
    /// 当前进程的句柄表（由外部设置，因为进程在 NRO 加载时才创建）
    /// </summary>
    public HandleTable? ActiveHandleTable { get; set; }

    /// <summary>
    /// 当前进程的 TLS 地址（IPC 缓冲区在 TLS + 0x100）
    /// </summary>
    public ulong ActiveTlsAddress { get; set; }

    public IpcBridge(IpcServiceManager serviceManager, VirtualMemoryManager memory)
    {
        _serviceManager = serviceManager;
        _memory = memory;
    }

    /// <summary>
    /// 绑定当前活跃进程的上下文
    /// 同时设置 IpcServiceManager.HandleTable，使 SmService.GetService 能创建真实句柄
    /// </summary>
    public void BindProcess(HorizonProcess process)
    {
        ActiveHandleTable = process.HandleTable;
        ActiveTlsAddress = process.TlsAddress;
        _serviceManager.HandleTable = process.HandleTable;
    }

    // ──────────────────── SvcConnectToNamedPort (0x1F) ────────────────────

    /// <summary>
    /// 处理 SvcConnectToNamedPort
    /// X0 = 指向服务名称的指针（null-terminated ASCII，8 字节对齐）
    /// 成功: X0 = 0 (Success), X1 = ClientSession 句柄
    /// 失败: X0 = 错误码
    /// </summary>
    public SvcResult ConnectToNamedPort(SvcInfo svc)
    {
        if (ActiveHandleTable == null)
        {
            Logger.Error(nameof(IpcBridge), "ConnectToNamedPort: 无活跃进程句柄表");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        ulong nameAddr = svc.X0;
        if (nameAddr == 0)
        {
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        string serviceName = ReadServiceNameFromGuest(nameAddr);
        Logger.Info(nameof(IpcBridge), $"ConnectToNamedPort: \"{serviceName}\" @ 0x{nameAddr:X16}");

        var service = _serviceManager.GetService(serviceName);
        if (service == null)
        {
            Logger.Warning(nameof(IpcBridge), $"服务未找到: \"{serviceName}\"");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.NotImplemented) };
        }

        var session = new KClientSession(serviceName);
        int handle = ActiveHandleTable.CreateHandle(session);

        Logger.Info(nameof(IpcBridge), $"连接服务 \"{serviceName}\" → 句柄 0x{handle:X8}");
        return new SvcResult
        {
            ReturnCode = ResultCode.Success,
            ReturnValue1 = (ulong)handle
        };
    }

    // ──────────────────── SvcSendSyncRequest (0x21) ────────────────────

    /// <summary>
    /// 处理 SvcSendSyncRequest (SVC 0x21)
    /// X0 = 会话句柄
    /// IPC 缓冲区固定在 TLS + 0x100
    /// </summary>
    public SvcResult SendSyncRequest(SvcInfo svc)
    {
        int sessionHandle = (int)svc.X0;
        ulong bufferAddr = ActiveTlsAddress != 0 ? ActiveTlsAddress + 0x100 : 0;
        return ProcessIpcRequest(sessionHandle, bufferAddr);
    }

    // ──────────────────── SvcSendSyncRequestWithUserBuffer (0x22) ────────────────────

    /// <summary>
    /// 处理 SvcSendSyncRequestWithUserBuffer (SVC 0x22)
    /// X0 = IPC 缓冲区地址（用户指定）
    /// X1 = 会话句柄
    /// 与 SVC 0x21 相同逻辑，但使用用户指定的缓冲区而非 TLS+0x100
    /// </summary>
    public SvcResult SendSyncRequestWithUserBuffer(SvcInfo svc)
    {
        ulong bufferAddr = svc.X0;
        int sessionHandle = (int)svc.X1;
        return ProcessIpcRequest(sessionHandle, bufferAddr);
    }

    // ──────────────────── SvcReplyAndReceive (0x43) ────────────────────

    /// <summary>
    /// 处理 SvcReplyAndReceive (SVC 0x43)
    /// X1 = 句柄数组指针
    /// X2 = 句柄数量
    /// X3 = 回复目标句柄（0 = 不回复）
    /// X4 = 超时（纳秒，-1 = 无限等待）
    /// 返回: X0 = 结果码, X1 = 就绪句柄的索引
    ///
    /// 在 HLE 模型中，NRO 是客户端而非服务端，
    /// 此 SVC 通常不被 NRO 直接调用。
    /// 实现为简化版本：处理回复后立即返回 SessionClosed。
    /// </summary>
    public SvcResult ReplyAndReceive(SvcInfo svc)
    {
        if (ActiveHandleTable == null)
        {
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        ulong handlesPtr = svc.X1;
        int numHandles = (int)svc.X2;
        int replyTarget = (int)svc.X3;
        long timeoutNs = (long)svc.X4;

        Logger.Debug(nameof(IpcBridge),
            $"ReplyAndReceive: handles=0x{handlesPtr:X16} count={numHandles} " +
            $"replyTarget=0x{replyTarget:X8} timeout={timeoutNs}");

        // 验证句柄数组指针
        if (numHandles > 0 && handlesPtr == 0)
        {
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 如果指定了回复目标，向该会话写入空回复
        if (replyTarget != 0)
        {
            var replySession = ActiveHandleTable.GetObject<KClientSession>(replyTarget);
            if (replySession != null)
            {
                Logger.Debug(nameof(IpcBridge),
                    $"ReplyAndReceive: 回复已同步完成 → handle=0x{replyTarget:X8} (\"{replySession.ServicePortName}\")");
            }
        }

        // 在 HLE 模型中，NRO 是客户端，没有其他客户端会向 NRO 发送请求。
        // 返回 TimedOut（而非 SessionClosed）以防止 guest 在循环中无限重试导致 CPU 热循环。
        // TimedOut 让 guest 认为等待超时，通常会退避或重试而非崩溃。
        return new SvcResult
        {
            ReturnCode = ResultCode.KernelResult(TKernelResult.TimedOut),
            ReturnValue1 = 0
        };
    }

    // ──────────────────── SvcReplyAndReceiveWithUserBuffer (0x44) ────────────────────

    /// <summary>
    /// 处理 SvcReplyAndReceiveWithUserBuffer (SVC 0x44)
    /// X1 = IPC 缓冲区地址
    /// X2 = IPC 缓冲区大小
    /// X3 = 句柄数组指针
    /// X4 = 句柄数量
    /// X5 = 回复目标句柄（0 = 不回复）
    /// X6 = 超时（纳秒，-1 = 无限等待）
    /// 返回: X0 = 结果码, X1 = 就绪句柄的索引
    ///
    /// 与 SVC 0x43 相同逻辑，但使用用户指定的缓冲区
    /// </summary>
    public SvcResult ReplyAndReceiveWithUserBuffer(SvcInfo svc)
    {
        if (ActiveHandleTable == null)
        {
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        ulong bufferAddr = svc.X1;
        ulong bufferSize = svc.X2;
        ulong handlesPtr = svc.X3;
        int numHandles = (int)svc.X4;
        int replyTarget = (int)svc.X5;
        long timeoutNs = (long)svc.X6;

        Logger.Debug(nameof(IpcBridge),
            $"ReplyAndReceiveWithUserBuffer: buf=0x{bufferAddr:X16} size=0x{bufferSize:X} " +
            $"handles=0x{handlesPtr:X16} count={numHandles} " +
            $"replyTarget=0x{replyTarget:X8} timeout={timeoutNs}");

        // 验证句柄数组指针
        if (numHandles > 0 && handlesPtr == 0)
        {
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 如果指定了回复目标，处理回复
        if (replyTarget != 0)
        {
            var replySession = ActiveHandleTable.GetObject<KClientSession>(replyTarget);
            if (replySession != null)
            {
                Logger.Debug(nameof(IpcBridge),
                    $"ReplyAndReceiveWithUserBuffer: 回复已同步完成 → handle=0x{replyTarget:X8}");
            }
        }

        // 返回 TimedOut 防止 guest CPU 热循环（同 ReplyAndReceive）
        return new SvcResult
        {
            ReturnCode = ResultCode.KernelResult(TKernelResult.TimedOut),
            ReturnValue1 = 0
        };
    }

    // ──────────────────── 核心 IPC 处理逻辑 ────────────────────

    /// <summary>
    /// 处理同步 IPC 请求（SendSyncRequest / SendSyncRequestWithUserBuffer 共用）
    /// 从指定缓冲区解析 IPC 请求、分发到 HLE 服务、写回响应
    /// </summary>
    private SvcResult ProcessIpcRequest(int sessionHandle, ulong bufferAddr)
    {
        if (ActiveHandleTable == null)
        {
            Logger.Error(nameof(IpcBridge), "ProcessIpcRequest: 无活跃进程句柄表");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }

        if (bufferAddr == 0)
        {
            Logger.Error(nameof(IpcBridge), "ProcessIpcRequest: IPC 缓冲区地址为 0");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidAddress) };
        }

        // 从句柄表查找会话对象，获取服务端口名称
        var session = ActiveHandleTable.GetObject<KClientSession>(sessionHandle);
        if (session == null)
        {
            Logger.Warning(nameof(IpcBridge), $"无效会话句柄 0x{sessionHandle:X8}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidHandle) };
        }

        string portName = session.ServicePortName;
        Logger.Info(nameof(IpcBridge),
            $"IPC 请求: handle=0x{sessionHandle:X8} → \"{portName}\" buf=0x{bufferAddr:X16} cmd={IpcMessageParser.ReadCommandId(bufferAddr, _memory)}");

        try
        {
            // 从 guest 内存解析 IPC 请求
            var request = IpcMessageParser.ParseRequest(bufferAddr, _memory);

            // 处理 Close 命令：关闭会话句柄
            if (request.Header.CommandType == IpcCommandType.Close ||
                request.Header.CommandType == IpcCommandType.TipcClose)
            {
                ActiveHandleTable.CloseHandle(sessionHandle);
                Logger.Info(nameof(IpcBridge), $"关闭会话: handle=0x{sessionHandle:X8} (\"{portName}\")");

                var closeResponse = new IpcResponse();
                IpcMessageParser.WriteResponse(bufferAddr, _memory, closeResponse, request.Header.CommandType);
                return new SvcResult { ReturnCode = ResultCode.Success };
            }

            // 分发到 HLE 服务处理
            var response = new IpcResponse();
            var result = _serviceManager.HandleRequest(portName, request, ref response);

            if (!result.IsSuccess)
            {
                Logger.Warning(nameof(IpcBridge),
                    $"IPC 请求失败: \"{portName}\" cmd={request.CommandId} → {result}");
                response.ResultCode = result;
            }

            if (_serviceManager.HandleTable == null)
            {
                Logger.Warning(nameof(IpcBridge),
                    "HandleTable 未设置，CopyHandles 中的句柄无法映射到 HandleTable。" +
                    "响应中的句柄将保持原始值，guest 可能无法使用这些句柄。");
            }

            // 将响应写回 guest 内存
            IpcMessageParser.WriteResponse(bufferAddr, _memory, response, request.Header.CommandType);

            Logger.Info(nameof(IpcBridge),
                $"IPC 完成: \"{portName}\" cmd={request.CommandId} → {result} " +
                $"data={response.Data.Count}B handles={response.CopyHandles.Count}");

            return new SvcResult { ReturnCode = ResultCode.Success };
        }
        catch (Exception ex)
        {
            Logger.Error(nameof(IpcBridge), $"IPC 处理异常: {ex}");
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.InvalidState) };
        }
    }

    // ──────────────────── 辅助方法 ────────────────────

    /// <summary>
    /// 从 guest 内存读取服务名称
    /// Horizon OS 服务名: null-terminated ASCII，最长 12 字节（含 null），8 字节对齐
    /// </summary>
    private string ReadServiceNameFromGuest(ulong address)
    {
        try
        {
            var buf = new byte[12];
            _memory.Read(address, buf);
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = 12;
            return Encoding.ASCII.GetString(buf, 0, len);
        }
        catch (Exception ex)
        {
            Logger.Warning(nameof(IpcBridge), $"读取服务名失败 @ 0x{address:X16}: {ex.Message}");
            return "";
        }
    }
}
