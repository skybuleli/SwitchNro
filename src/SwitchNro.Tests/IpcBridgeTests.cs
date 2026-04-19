using System;
using System.Buffers.Binary;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Cpu;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using Xunit;

namespace SwitchNro.Tests;

public class IpcBridgeTests : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private readonly IpcServiceManager _serviceManager;
    private readonly IpcBridge _bridge;
    private readonly HandleTable _handleTable;
    private const ulong TlsBase = 0x1000_0000;
    private const ulong IpcBufferAddr = TlsBase + 0x100;

    public IpcBridgeTests()
    {
        _memory = new VirtualMemoryManager();
        _serviceManager = new IpcServiceManager();
        _bridge = new IpcBridge(_serviceManager, _memory);
        _handleTable = new HandleTable();

        // 映射 TLS 和 IPC 缓冲区区域
        _memory.MapZero(TlsBase, 0x200, MemoryPermissions.ReadWrite);

        // 绑定 IpcBridge 到模拟的进程上下文
        _bridge.ActiveHandleTable = _handleTable;
        _bridge.ActiveTlsAddress = TlsBase;

        // 注册 sm: 服务（核心必选服务）
        _serviceManager.RegisterService(new SmService(_serviceManager));
        // 注册一个测试服务
        _serviceManager.RegisterService(new SettingsService());
    }

    public void Dispose() => _memory.Dispose();

    // ──────────────────── ConnectToNamedPort 测试 ────────────────────

    [Fact]
    public void ConnectToNamedPort_ValidService_ReturnsHandle()
    {
        _memory.MapZero(0x5000_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5000_0000, "sm:");

        var svc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5000_0000 };
        var result = _bridge.ConnectToNamedPort(svc);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.NotEqual(0UL, result.ReturnValue1);

        // 句柄应该在 HandleTable 中
        int handle = (int)result.ReturnValue1;
        var session = _handleTable.GetObject<KClientSession>(handle);
        Assert.NotNull(session);
        Assert.Equal("sm:", session!.ServicePortName);
    }

    [Fact]
    public void ConnectToNamedPort_UnknownService_ReturnsError()
    {
        _memory.MapZero(0x5001_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5001_0000, "unknown:");

        var svc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5001_0000 };
        var result = _bridge.ConnectToNamedPort(svc);

        Assert.False(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void ConnectToNamedPort_NullPointer_ReturnsError()
    {
        var svc = new SvcInfo { SvcNumber = 0x1F, X0 = 0 };
        var result = _bridge.ConnectToNamedPort(svc);

        Assert.False(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void ConnectToNamedPort_MultipleConnections_ReturnsDifferentHandles()
    {
        _memory.MapZero(0x5002_0000, 0x1000, MemoryPermissions.ReadWrite);

        WriteServiceName(0x5002_0000, "sm:");
        var svc1 = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5002_0000 };
        var result1 = _bridge.ConnectToNamedPort(svc1);

        WriteServiceName(0x5002_0000, "set:");
        var svc2 = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5002_0000 };
        var result2 = _bridge.ConnectToNamedPort(svc2);

        Assert.NotEqual(result1.ReturnValue1, result2.ReturnValue1);

        var s1 = _handleTable.GetObject<KClientSession>((int)result1.ReturnValue1);
        var s2 = _handleTable.GetObject<KClientSession>((int)result2.ReturnValue1);
        Assert.Equal("sm:", s1!.ServicePortName);
        Assert.Equal("set:", s2!.ServicePortName);
    }

    // ──────────────────── SendSyncRequest 测试 ────────────────────

    [Fact]
    public void SendSyncRequest_InitializeSm_ReturnsSuccess()
    {
        // 先连接 sm: 服务
        _memory.MapZero(0x5003_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5003_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5003_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // 构造 sm:Initialize IPC 请求 (commandId=0, CMIF Request type=5)
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0, copyHandles: 0);

        // 发送 SendSyncRequest
        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        Assert.True(sendResult.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SendSyncRequest_InvalidHandle_ReturnsError()
    {
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0, copyHandles: 0);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = 0xDEAD };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        Assert.False(sendResult.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SendSyncRequest_CloseSession_ClosesHandle()
    {
        _memory.MapZero(0x5004_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5004_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5004_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        Assert.True(_handleTable.IsValid(sessionHandle));

        // 构造 Close IPC 请求 (type=4)
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Close, commandId: 0, copyHandles: 0);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        Assert.True(sendResult.ReturnCode.IsSuccess);
        Assert.False(_handleTable.IsValid(sessionHandle)); // 句柄已被关闭
    }

    [Fact]
    public void SendSyncRequest_SmGetService_ReturnsServiceHandle()
    {
        // 设置 HandleTable — 模拟真实运行时（BindProcess 会设置此属性）
        _serviceManager.HandleTable = _handleTable;

        // 先连接 sm:
        _memory.MapZero(0x5005_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5005_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5005_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int smHandle = (int)connResult.ReturnValue1;

        // 构造 sm:GetService 请求 (commandId=1)
        // Data 格式: [4字节 padding] + [8字节 服务名 "set:\0\0\0\0\0"]
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0); // padding
        var nameBytes = Encoding.ASCII.GetBytes("set:");
        Array.Copy(nameBytes, 0, data, 4, nameBytes.Length);
        WriteCmifRequestWithData(IpcBufferAddr, IpcCommandType.Request, commandId: 1, data: data);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)smHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        Assert.True(sendResult.ReturnCode.IsSuccess);

        // 验证响应包含 CopyHandle（服务句柄）
        // New format: Word1 bit[31] = HndDescEnable, handles in IpcHandleDesc at offset 0x08
        var responseWord1 = _memory.Read<uint>(IpcBufferAddr + 4);
        bool hasHndDesc = (responseWord1 & 0x80000000u) != 0;
        Assert.True(hasHndDesc, "sm:GetService 应返回句柄描述符");

        // IpcHandleDesc at offset 0x08: word (HasPId[0] | CopyCount[1:4] | MoveCount[5:8])
        uint hndWord = _memory.Read<uint>(IpcBufferAddr + 0x08);
        int copyHandleCount = (int)((hndWord >> 1) & 0xF);
        Assert.True(copyHandleCount > 0, "sm:GetService 应返回至少一个 CopyHandle");

        // Copy handles start after hndWord (+ PID if HasPId)
        int hndOffset = 0x08 + 4; // After hndWord (no PID)
        int serviceHandle = (int)_memory.Read<uint>(IpcBufferAddr + (ulong)hndOffset);
        Assert.True(_handleTable.IsValid(serviceHandle));

        var serviceSession = _handleTable.GetObject<KClientSession>(serviceHandle);
        Assert.NotNull(serviceSession);
        Assert.Equal("set:", serviceSession!.ServicePortName);
    }

    [Fact]
    public void SendSyncRequest_NoHandleTable_WarningPath()
    {
        // HandleTable 未设置 — SmService 返回错误而非无效句柄
        // IpcBridge 记录警告但仍然写回响应

        // 先连接 sm:
        _memory.MapZero(0x5006_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5006_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5006_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int smHandle = (int)connResult.ReturnValue1;

        // 构造 sm:GetService 请求 (commandId=1) 但 HandleTable 为 null
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0); // padding
        var nameBytes = Encoding.ASCII.GetBytes("set:");
        Array.Copy(nameBytes, 0, data, 4, nameBytes.Length);
        WriteCmifRequestWithData(IpcBufferAddr, IpcCommandType.Request, commandId: 1, data: data);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)smHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        // SVC 本身成功（IPC 传输成功），但 sm:GetService 返回错误码
        Assert.True(sendResult.ReturnCode.IsSuccess); // SVC 层成功

        // 验证响应中没有句柄描述符（工厂未设置，服务返回错误）
        var responseWord1 = _memory.Read<uint>(IpcBufferAddr + 4);
        bool hasHndDesc = (responseWord1 & 0x80000000u) != 0;
        Assert.False(hasHndDesc); // 没有返回句柄描述符
    }

    // ──────────────────── SendSyncRequestWithUserBuffer (SVC 0x22) 测试 ────────────────────

    [Fact]
    public void SendSyncRequestWithUserBuffer_UsesUserBuffer()
    {
        // 映射用户指定的 IPC 缓冲区（非 TLS+0x100）
        ulong userBuffer = 0x6000_0000;
        _memory.MapZero(userBuffer, 0x100, MemoryPermissions.ReadWrite);

        // 连接 sm: 服务
        _memory.MapZero(0x5007_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5007_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5007_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // 在用户缓冲区写入 IPC 请求
        WriteCmifRequest(userBuffer, IpcCommandType.Request, commandId: 0, copyHandles: 0);

        // SVC 0x22: X0=buffer_addr, X1=session_handle
        var svc = new SvcInfo { SvcNumber = 0x22, X0 = userBuffer, X1 = (ulong)sessionHandle };
        var result = _bridge.SendSyncRequestWithUserBuffer(svc);

        Assert.True(result.ReturnCode.IsSuccess);

        // 验证响应写回用户缓冲区（而非 TLS）
        var responseHeader = _memory.Read<uint>(userBuffer);
        var cmdType = (IpcCommandType)(responseHeader & 0xF);
        Assert.Equal(IpcCommandType.Request, cmdType); // 响应类型匹配请求
    }

    [Fact]
    public void SendSyncRequestWithUserBuffer_InvalidHandle_ReturnsError()
    {
        ulong userBuffer = 0x6001_0000;
        _memory.MapZero(userBuffer, 0x100, MemoryPermissions.ReadWrite);
        WriteCmifRequest(userBuffer, IpcCommandType.Request, commandId: 0, copyHandles: 0);

        var svc = new SvcInfo { SvcNumber = 0x22, X0 = userBuffer, X1 = 0xDEAD };
        var result = _bridge.SendSyncRequestWithUserBuffer(svc);

        Assert.False(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void SendSyncRequestWithUserBuffer_ZeroBuffer_ReturnsError()
    {
        _memory.MapZero(0x5008_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5008_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5008_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // X0=0（空缓冲区地址）
        var svc = new SvcInfo { SvcNumber = 0x22, X0 = 0, X1 = (ulong)sessionHandle };
        var result = _bridge.SendSyncRequestWithUserBuffer(svc);

        Assert.False(result.ReturnCode.IsSuccess);
    }

    // ──────────────────── ReplyAndReceive (SVC 0x43) 测试 ────────────────────

    [Fact]
    public void ReplyAndReceive_NoPendingRequests_ReturnsTimedOut()
    {
        // 在 HLE 模型中，NRO 是客户端，没有其他客户端会向 NRO 发送请求
        // 返回 TimedOut 防止 guest 在循环中无限重试导致 CPU 热循环
        var svc = new SvcInfo { SvcNumber = 0x43, X1 = 0, X2 = 0, X3 = 0, X4 = 0 };
        var result = _bridge.ReplyAndReceive(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    [Fact]
    public void ReplyAndReceive_WithReplyTarget_ProcessesReply()
    {
        // 创建一个会话句柄作为回复目标
        _memory.MapZero(0x5009_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5009_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5009_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // 指定回复目标但仍然没有挂起请求
        var svc = new SvcInfo { SvcNumber = 0x43, X1 = 0, X2 = 0, X3 = (ulong)sessionHandle, X4 = 0 };
        var result = _bridge.ReplyAndReceive(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    // ──────────────────── ReplyAndReceiveWithUserBuffer (SVC 0x44) 测试 ────────────────────

    [Fact]
    public void ReplyAndReceiveWithUserBuffer_NoPendingRequests_ReturnsTimedOut()
    {
        ulong userBuffer = 0x6002_0000;
        _memory.MapZero(userBuffer, 0x100, MemoryPermissions.ReadWrite);

        var svc = new SvcInfo
        {
            SvcNumber = 0x44,
            X1 = userBuffer,
            X2 = 0x100,
            X3 = 0,
            X4 = 0,
            X5 = 0,
            X6 = 0
        };
        var result = _bridge.ReplyAndReceiveWithUserBuffer(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    [Fact]
    public void ReplyAndReceiveWithUserBuffer_WithReplyTarget_ReturnsTimedOut()
    {
        ulong userBuffer = 0x6003_0000;
        _memory.MapZero(userBuffer, 0x100, MemoryPermissions.ReadWrite);

        // 创建一个会话句柄作为回复目标
        _memory.MapZero(0x500A_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x500A_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x500A_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        var svc = new SvcInfo
        {
            SvcNumber = 0x44,
            X1 = userBuffer,
            X2 = 0x100,
            X3 = 0,
            X4 = 0,
            X5 = (ulong)sessionHandle,
            X6 = 0
        };
        var result = _bridge.ReplyAndReceiveWithUserBuffer(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    // ──────────────────── ConnectToNamedPort 扩展测试 ────────────────────

    [Fact]
    public void ConnectToNamedPort_EmptyName_ReturnsError()
    {
        // 写入空字符串（只有 null 终止符）
        _memory.MapZero(0x5010_0000, 0x1000, MemoryPermissions.ReadWrite);
        _memory.Write(0x5010_0000, new byte[] { 0 });

        var svc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5010_0000 };
        var result = _bridge.ConnectToNamedPort(svc);

        // 空服务名应返回错误（服务不存在）
        Assert.False(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void ConnectToNamedPort_LongName_TruncatedTo12Bytes()
    {
        // 服务名超过 12 字节限制 — 应被截断，找不到匹配服务
        _memory.MapZero(0x5011_0000, 0x1000, MemoryPermissions.ReadWrite);
        var longName = Encoding.ASCII.GetBytes("very_long_service_name_that_exceeds_limit");
        _memory.Write(0x5011_0000, longName);

        var svc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5011_0000 };
        var result = _bridge.ConnectToNamedPort(svc);

        // 截断后名称不匹配任何已注册服务
        Assert.False(result.ReturnCode.IsSuccess);
    }

    [Fact]
    public void ConnectToNamedPort_NoHandleTable_ReturnsInvalidState()
    {
        _memory.MapZero(0x5012_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5012_0000, "sm:");

        // 临时移除句柄表
        _bridge.ActiveHandleTable = null;

        var svc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5012_0000 };
        var result = _bridge.ConnectToNamedPort(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);

        // 恢复句柄表供后续测试使用
        _bridge.ActiveHandleTable = _handleTable;
    }

    [Fact]
    public void ConnectToNamedPort_ReconnectSameService_CreatesNewSession()
    {
        _memory.MapZero(0x5013_0000, 0x1000, MemoryPermissions.ReadWrite);

        // 第一次连接
        WriteServiceName(0x5013_0000, "sm:");
        var svc1 = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5013_0000 };
        var result1 = _bridge.ConnectToNamedPort(svc1);
        int handle1 = (int)result1.ReturnValue1;

        // 第二次连接同一服务
        var svc2 = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5013_0000 };
        var result2 = _bridge.ConnectToNamedPort(svc2);
        int handle2 = (int)result2.ReturnValue1;

        // 两个句柄都有效但不同（不同的 KClientSession 实例）
        Assert.True(result1.ReturnCode.IsSuccess);
        Assert.True(result2.ReturnCode.IsSuccess);
        Assert.NotEqual(handle1, handle2);
        Assert.True(_handleTable.IsValid(handle1));
        Assert.True(_handleTable.IsValid(handle2));
    }

    // ──────────────────── SendSyncRequest 扩展测试 ────────────────────

    [Fact]
    public void SendSyncRequest_TipcFormat_ReturnsSuccess()
    {
        // 连接 sm: 服务
        _memory.MapZero(0x5020_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5020_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5020_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // 构造 TIPC 请求: header[3:0]=TipcRequest(10), header[31:16]=commandId
        // TipcRequest 初始化命令: commandId=0
        uint tipcHeader = ((uint)IpcCommandType.TipcRequest & 0xF) | (0u << 16); // cmdId=0
        _memory.Write(IpcBufferAddr, tipcHeader);
        _memory.Write(IpcBufferAddr + 0x04, 0u); // padding
        _memory.Write(IpcBufferAddr + 0x08, 0u); // padding
        _memory.Write(IpcBufferAddr + 0x0C, 0u); // padding
        // TIPC 数据在 offset 0x10 之后
        _memory.Write(IpcBufferAddr + 0x10, 0u);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        Assert.True(sendResult.ReturnCode.IsSuccess);

        // 验证 TIPC 响应格式: header[3:0] = TipcRequest(10)
        var responseHeader = _memory.Read<uint>(IpcBufferAddr);
        var responseType = (IpcCommandType)(responseHeader & 0xF);
        Assert.Equal(IpcCommandType.TipcRequest, responseType);
    }

    [Fact]
    public void SendSyncRequest_SequentialRequests_SameSession()
    {
        // 连接 sm: 服务
        _memory.MapZero(0x5021_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5021_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5021_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // 第一次请求: sm:Initialize (cmdId=0)
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0, copyHandles: 0);
        var send1 = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var result1 = _bridge.SendSyncRequest(send1);
        Assert.True(result1.ReturnCode.IsSuccess);

        // 第二次请求: sm:Initialize (cmdId=0) 在同一会话上
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0, copyHandles: 0);
        var send2 = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var result2 = _bridge.SendSyncRequest(send2);
        Assert.True(result2.ReturnCode.IsSuccess);

        // 会话仍然有效
        Assert.True(_handleTable.IsValid(sessionHandle));
    }

    [Fact]
    public void SendSyncRequest_UnknownCommandId_ReturnsServiceError()
    {
        // 连接 sm: 服务
        _memory.MapZero(0x5022_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5022_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5022_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        // 发送未实现的命令 ID (0xFF = 不存在于 sm: 命令表)
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0xFF, copyHandles: 0);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        // SVC 层成功（IPC 传输成功），但响应中的 result code 为非零
        Assert.True(sendResult.ReturnCode.IsSuccess); // SVC 成功

        // 读取响应中的 result code
        // 新格式: CmifHeaderSize(8) = 数据从 0x08 开始（无句柄描述符时）
        var responseDataStart = IpcBufferAddr + 0x08;
        var resultCodeValue = _memory.Read<int>(responseDataStart);
        // SfResult(2) = Module=10, Description=2 (Command not found)
        Assert.NotEqual(0, resultCodeValue); // 非成功
    }

    [Fact]
    public void SendSyncRequest_ZeroTlsAddress_ReturnsInvalidAddress()
    {
        // 临时设置 TLS 地址为 0
        var originalTls = _bridge.ActiveTlsAddress;
        _bridge.ActiveTlsAddress = 0;

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = 1 };
        var result = _bridge.SendSyncRequest(sendSvc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);

        // 恢复 TLS 地址
        _bridge.ActiveTlsAddress = originalTls;
    }

    [Fact]
    public void SendSyncRequest_TipcClose_ClosesHandle()
    {
        // 连接 sm: 服务
        _memory.MapZero(0x5023_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5023_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5023_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        int sessionHandle = (int)connResult.ReturnValue1;

        Assert.True(_handleTable.IsValid(sessionHandle));

        // 构造 TIPC Close 请求 (type=9)
        uint tipcCloseHeader = (uint)IpcCommandType.TipcClose & 0xFFFF;
        _memory.Write(IpcBufferAddr, tipcCloseHeader);
        _memory.Write(IpcBufferAddr + 4, 0u);

        var sendSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)sessionHandle };
        var sendResult = _bridge.SendSyncRequest(sendSvc);

        Assert.True(sendResult.ReturnCode.IsSuccess);
        Assert.False(_handleTable.IsValid(sessionHandle)); // 句柄已被关闭
    }

    // ──────────────────── ReplyAndReceive 扩展测试 ────────────────────

    [Fact]
    public void ReplyAndReceive_InvalidHandlePtr_ReturnsInvalidAddress()
    {
        // numHandles > 0 但 handlesPtr = 0
        var svc = new SvcInfo { SvcNumber = 0x43, X1 = 0, X2 = 2, X3 = 0, X4 = 0 };
        var result = _bridge.ReplyAndReceive(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    [Fact]
    public void ReplyAndReceive_InvalidReplyTarget_NoCrash()
    {
        // 回复目标句柄无效 — 不应崩溃
        var svc = new SvcInfo { SvcNumber = 0x43, X1 = 0, X2 = 0, X3 = 0xDEAD, X4 = 0 };
        var result = _bridge.ReplyAndReceive(svc);

        // 应返回 TimedOut（无效回复目标不影响返回值）
        Assert.Equal(ResultCode.KernelResult(TKernelResult.TimedOut), result.ReturnCode);
    }

    [Fact]
    public void ReplyAndReceive_NoHandleTable_ReturnsInvalidState()
    {
        var originalTable = _bridge.ActiveHandleTable;
        _bridge.ActiveHandleTable = null;

        var svc = new SvcInfo { SvcNumber = 0x43, X1 = 0, X2 = 0, X3 = 0, X4 = 0 };
        var result = _bridge.ReplyAndReceive(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidState), result.ReturnCode);

        _bridge.ActiveHandleTable = originalTable;
    }

    [Fact]
    public void ReplyAndReceiveWithUserBuffer_InvalidHandlePtr_ReturnsInvalidAddress()
    {
        ulong userBuffer = 0x6010_0000;
        _memory.MapZero(userBuffer, 0x100, MemoryPermissions.ReadWrite);

        // numHandles > 0 但 handlesPtr = 0
        var svc = new SvcInfo
        {
            SvcNumber = 0x44,
            X1 = userBuffer,
            X2 = 0x100,
            X3 = 0,     // handlesPtr = 0
            X4 = 2,     // numHandles > 0
            X5 = 0,
            X6 = 0
        };
        var result = _bridge.ReplyAndReceiveWithUserBuffer(svc);

        Assert.Equal(ResultCode.KernelResult(TKernelResult.InvalidAddress), result.ReturnCode);
    }

    // ──────────────────── Integration: Full IPC Flow ────────────────────

    [Fact]
    public void Integration_FullIpcFlow_ConnectGetServiceSubRequestClose()
    {
        // 设置 HandleTable — 模拟真实运行时（BindProcess 会设置此属性）
        _serviceManager.HandleTable = _handleTable;

        // 1. ConnectToNamedPort("sm:")
        _memory.MapZero(0x5030_0000, 0x1000, MemoryPermissions.ReadWrite);
        WriteServiceName(0x5030_0000, "sm:");
        var connSvc = new SvcInfo { SvcNumber = 0x1F, X0 = 0x5030_0000 };
        var connResult = _bridge.ConnectToNamedPort(connSvc);
        Assert.True(connResult.ReturnCode.IsSuccess);
        int smHandle = (int)connResult.ReturnValue1;

        // 2. SendSyncRequest: sm:Initialize (cmdId=0)
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0, copyHandles: 0);
        var initSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)smHandle };
        var initResult = _bridge.SendSyncRequest(initSvc);
        Assert.True(initResult.ReturnCode.IsSuccess);

        // 3. SendSyncRequest: sm:GetService("set:") → 获取 set: 服务句柄
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0); // padding
        var nameBytes = Encoding.ASCII.GetBytes("set:");
        Array.Copy(nameBytes, 0, data, 4, nameBytes.Length);
        WriteCmifRequestWithData(IpcBufferAddr, IpcCommandType.Request, commandId: 1, data: data);
        var getServSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)smHandle };
        var getServResult = _bridge.SendSyncRequest(getServSvc);
        Assert.True(getServResult.ReturnCode.IsSuccess);

        // 验证响应包含 CopyHandle（服务句柄）
        var responseWord1 = _memory.Read<uint>(IpcBufferAddr + 4);
        bool hasHndDesc = (responseWord1 & 0x80000000u) != 0;
        Assert.True(hasHndDesc, "sm:GetService 应返回句柄描述符");

        // IpcHandleDesc: read copy handle
        uint hndWord = _memory.Read<uint>(IpcBufferAddr + 0x08);
        int copyHandleCount = (int)((hndWord >> 1) & 0xF);
        Assert.True(copyHandleCount > 0, "sm:GetService 应返回至少一个 CopyHandle");

        int hndOffset = 0x08 + 4;
        int setHandle = (int)_memory.Read<uint>(IpcBufferAddr + (ulong)hndOffset);
        Assert.True(_handleTable.IsValid(setHandle));

        // 4. SendSyncRequest: 向 set: 服务发送请求
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Request, commandId: 0, copyHandles: 0);
        var setSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)setHandle };
        var setResult = _bridge.SendSyncRequest(setSvc);
        Assert.True(setResult.ReturnCode.IsSuccess);

        // 5. 关闭 set: 会话
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Close, commandId: 0, copyHandles: 0);
        var closeSetSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)setHandle };
        var closeSetResult = _bridge.SendSyncRequest(closeSetSvc);
        Assert.True(closeSetResult.ReturnCode.IsSuccess);
        Assert.False(_handleTable.IsValid(setHandle)); // 已关闭

        // 6. 关闭 sm: 会话
        WriteCmifRequest(IpcBufferAddr, IpcCommandType.Close, commandId: 0, copyHandles: 0);
        var closeSmSvc = new SvcInfo { SvcNumber = 0x21, X0 = (ulong)smHandle };
        var closeSmResult = _bridge.SendSyncRequest(closeSmSvc);
        Assert.True(closeSmResult.ReturnCode.IsSuccess);
        Assert.False(_handleTable.IsValid(smHandle)); // 已关闭
    }

    // ──────────────────── 辅助方法 ────────────────────

    private void WriteServiceName(ulong addr, string name)
    {
        var bytes = new byte[12]; // Horizon 服务名最长 12 字节
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, 0, bytes, 0, Math.Min(nameBytes.Length, 12));
        _memory.Write(addr, bytes);
    }

    private void WriteCmifRequest(ulong addr, IpcCommandType cmdType, uint commandId, int copyHandles)
    {
        // CMIF 2-word header:
        //   Word0: Type[0:15] | X_count[16:19] | A_count[20:23] | B_count[24:27] | W_count[28:31]
        //   Word1: RawDataSize[0:9] | RecvListFlags[10:13] | Reserved[14:30] | HndDescEnable[31]
        uint word0 = (uint)cmdType & 0xFFFF;
        uint word1 = 1u; // dataSizeWords=1 (commandId)

        int offset = 0x08; // After 2-word header

        if (copyHandles > 0)
        {
            word1 |= 0x80000000u; // HndDescEnable
            // Handle descriptor: HasPId=0 | CopyCount[1:4] | MoveCount[5:8]
            uint hndWord = (uint)(copyHandles & 0xF) << 1;
            _memory.Write(addr + (ulong)offset, hndWord);
            offset += 4;
            offset += copyHandles * 4;
        }

        _memory.Write(addr, word0);
        _memory.Write(addr + 0x04, word1);

        // 16-byte alignment for rawData
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        _memory.Write(addr + (ulong)offset, commandId);
    }

    private void WriteCmifRequestWithData(ulong addr, IpcCommandType cmdType, uint commandId, byte[] data)
    {
        int dataSizeWords = (4 + data.Length + 3) / 4; // commandId(4) + data, aligned to 4

        uint word0 = (uint)cmdType & 0xFFFF;
        uint word1 = (uint)(dataSizeWords & 0x3FF); // dataSizeWords
        _memory.Write(addr, word0);
        _memory.Write(addr + 0x04, word1);

        // After 2-word header (8 bytes), parser applies 16-byte alignment: pad=8
        // RawData starts at offset 0x10
        int offset = 0x10;
        _memory.Write(addr + (ulong)offset, commandId);
        offset += 4;

        // data payload
        if (data.Length > 0)
        {
            var padded = new byte[dataSizeWords * 4 - 4]; // minus commandId's 4 bytes
            Array.Copy(data, 0, padded, 0, Math.Min(data.Length, padded.Length));
            _memory.Write(addr + (ulong)offset, padded);
        }
    }
}
