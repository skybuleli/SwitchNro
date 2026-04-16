using System;
using SwitchNro.Common;

namespace SwitchNro.HLE.Ipc;

/// <summary>
/// IPC 消息头
/// 遵循 Horizon OS 的 IPC 二进制协议格式
/// </summary>
public readonly struct IpcMessageHeader
{
    /// <summary>原始 32 位值</summary>
    public uint Value { get; init; }

    public IpcMessageHeader(uint value) => Value = value;

    /// <summary>命令类型</summary>
    public IpcCommandType CommandType => (IpcCommandType)(Value & 0xF);

    /// <summary>数据字数（不包含头）</summary>
    public int DataWordCount => (int)((Value >> 16) & 0xFFFF);

    /// <summary>请求命令 ID</summary>
    public uint CommandId => (Value >> 16) & 0xFFFF;
}

/// <summary>IPC 命令类型</summary>
public enum IpcCommandType : uint
{
    Invalid = 0,
    LegacyControl = 1,
    LegacyRequest = 2,
    Close = 4,
    Request = 5,
    Control = 6,
    RequestWithContext = 7,
    ControlWithContext = 8,
    TipcClose = 9,
    TipcRequest = 10,
    TipcControl = 11,
}

/// <summary>IPC 请求</summary>
public sealed class IpcRequest
{
    /// <summary>消息头</summary>
    public IpcMessageHeader Header { get; init; }

    /// <summary>命令 ID</summary>
    public uint CommandId { get; init; }

    /// <summary>客户端进程 ID</summary>
    public ulong ClientPid { get; init; }

    /// <summary>请求数据缓冲区</summary>
    public byte[] Data { get; init; } = [];

    /// <summary>已拷贝句柄</summary>
    public int[] CopyHandles { get; init; } = [];

    /// <summary>已移动句柄</summary>
    public int[] MoveHandles { get; init; } = [];
}

/// <summary>IPC 响应</summary>
public sealed class IpcResponse
{
    /// <summary>结果码</summary>
    public ResultCode ResultCode { get; set; } = ResultCode.Success;

    /// <summary>响应数据</summary>
    public List<byte> Data { get; } = new();

    /// <summary>已拷贝句柄</summary>
    public List<int> CopyHandles { get; } = new();
}
