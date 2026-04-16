using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// bsd: — BSD Socket 服务 (Network Sockets)
/// 提供 POSIX 兼容的 BSD 套接字接口
/// Homebrew 使用此服务进行网络通信 (TCP/UDP)
/// </summary>
public sealed class SocketService : IIpcService
{
    public string PortName => "bsd:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>虚拟文件描述符表</summary>
    private readonly Dictionary<int, BsdSocket> _sockets = new();
    private int _nextFd = 0x20;

    public SocketService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = InitializeBsd,              // 初始化 BSD
            [1]  = Open,                       // 创建套接字
            [2]  = Close,                      // 关闭套接字
            [3]  = Connect,                    // 连接
            [4]  = Bind,                       // 绑定
            [5]  = Listen,                     // 监听
            [6]  = Accept,                     // 接受连接
            [7]  = Recv,                       // 接收数据
            [8]  = Send,                       // 发送数据
            [9]  = RecvFrom,                   // 从指定地址接收
            [10] = SendTo,                     // 发送到指定地址
            [11] = SetSockOpt,                 // 设置套接字选项
            [12] = GetSockOpt,                 // 获取套接字选项
            [13] = Poll,                       // 轮询
            [14] = Shutdown,                   // 关闭连接
            [15] = GetSockName,                // 获取套接字名称
            [16] = GetPeerName,                // 获取对端名称
            [17] = Ioctl,                      // Ioctl
            [18] = Fcntl,                      // Fcntl
            [20] = Select,                     // Select
            [21] = Write,                      // 写入
            [22] = Read,                       // 读取
            [50] = GetErrno,                   // 获取错误码
            [51] = ShutdownAllSockets,         // 关闭所有套接字
        };
    }

    /// <summary>命令 0: InitializeBsd — 初始化 BSD 套接字子系统</summary>
    private ResultCode InitializeBsd(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(SocketService), "bsd: InitializeBsd");
        // 返回工作缓冲区大小
        response.Data.AddRange(BitConverter.GetBytes(0x4000)); // 16KB 工作缓冲区
        return ResultCode.Success;
    }

    /// <summary>命令 1: Open — 创建套接字</summary>
    private ResultCode Open(IpcRequest request, ref IpcResponse response)
    {
        // 读取参数: domain, type, protocol
        int domain = request.Data.Length >= 4 ? BitConverter.ToInt32(request.Data, 0) : 2;  // AF_INET
        int type = request.Data.Length >= 8 ? BitConverter.ToInt32(request.Data, 4) : 1;     // SOCK_STREAM
        int protocol = request.Data.Length >= 12 ? BitConverter.ToInt32(request.Data, 8) : 0;

        int fd = _nextFd++;
        _sockets[fd] = new BsdSocket
        {
            Domain = domain,
            Type = type,
            Protocol = protocol,
            IsConnected = false,
        };

        Logger.Info(nameof(SocketService), $"bsd: Open(domain={domain}, type={type}, proto={protocol}) → fd={fd}");
        response.Data.AddRange(BitConverter.GetBytes(fd));
        return ResultCode.Success;
    }

    /// <summary>命令 2: Close — 关闭套接字</summary>
    private ResultCode Close(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length >= 4)
        {
            int fd = BitConverter.ToInt32(request.Data, 0);
            _sockets.Remove(fd);
            Logger.Debug(nameof(SocketService), $"bsd: Close(fd={fd})");
        }
        response.Data.AddRange(BitConverter.GetBytes(0)); // 返回值
        return ResultCode.Success;
    }

    /// <summary>命令 3: Connect — 连接到远程主机</summary>
    private ResultCode Connect(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length >= 4)
        {
            int fd = BitConverter.ToInt32(request.Data, 0);
            if (_sockets.TryGetValue(fd, out var socket))
            {
                socket.IsConnected = true;
                Logger.Info(nameof(SocketService), $"bsd: Connect(fd={fd})");
            }
        }
        response.Data.AddRange(BitConverter.GetBytes(0)); // 成功
        return ResultCode.Success;
    }

    /// <summary>命令 4: Bind — 绑定地址</summary>
    private ResultCode Bind(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Bind");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 5: Listen — 开始监听</summary>
    private ResultCode Listen(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Listen");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 6: Accept — 接受连接</summary>
    private ResultCode Accept(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Accept");
        // 返回新的文件描述符
        int newFd = _nextFd++;
        response.Data.AddRange(BitConverter.GetBytes(newFd));
        return ResultCode.Success;
    }

    /// <summary>命令 7: Recv — 接收数据</summary>
    private ResultCode Recv(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Recv");
        response.Data.AddRange(BitConverter.GetBytes(0)); // 0 bytes received
        return ResultCode.Success;
    }

    /// <summary>命令 8: Send — 发送数据</summary>
    private ResultCode Send(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Send");
        // 返回发送的字节数 (假设全部发送成功)
        response.Data.AddRange(BitConverter.GetBytes(request.Data.Length));
        return ResultCode.Success;
    }

    /// <summary>命令 9: RecvFrom — 从指定地址接收</summary>
    private ResultCode RecvFrom(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: RecvFrom");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 10: SendTo — 发送到指定地址</summary>
    private ResultCode SendTo(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: SendTo");
        response.Data.AddRange(BitConverter.GetBytes(request.Data.Length));
        return ResultCode.Success;
    }

    /// <summary>命令 11: SetSockOpt — 设置套接字选项</summary>
    private ResultCode SetSockOpt(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: SetSockOpt");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 12: GetSockOpt — 获取套接字选项</summary>
    private ResultCode GetSockOpt(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: GetSockOpt");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 13: Poll — 轮询套接字状态</summary>
    private ResultCode Poll(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Poll");
        response.Data.AddRange(BitConverter.GetBytes(0)); // 无就绪事件
        return ResultCode.Success;
    }

    /// <summary>命令 14: Shutdown — 关闭连接方向</summary>
    private ResultCode Shutdown(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Shutdown");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 15: GetSockName — 获取本地套接字地址</summary>
    private ResultCode GetSockName(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: GetSockName");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 16: GetPeerName — 获取远端套接字地址</summary>
    private ResultCode GetPeerName(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: GetPeerName");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 17: Ioctl</summary>
    private ResultCode Ioctl(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Ioctl");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 18: Fcntl</summary>
    private ResultCode Fcntl(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Fcntl");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 20: Select</summary>
    private ResultCode Select(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Select");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 21: Write</summary>
    private ResultCode Write(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Write");
        response.Data.AddRange(BitConverter.GetBytes(request.Data.Length));
        return ResultCode.Success;
    }

    /// <summary>命令 22: Read</summary>
    private ResultCode Read(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SocketService), "bsd: Read");
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 50: GetErrno — 获取最后一次错误码</summary>
    private ResultCode GetErrno(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0)); // 无错误
        return ResultCode.Success;
    }

    /// <summary>命令 51: ShutdownAllSockets — 关闭所有套接字</summary>
    private ResultCode ShutdownAllSockets(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(SocketService), $"bsd: ShutdownAllSockets ({_sockets.Count} sockets)");
        _sockets.Clear();
        return ResultCode.Success;
    }

    public void Dispose()
    {
        _sockets.Clear();
    }
}

/// <summary>BSD 套接字状态</summary>
internal sealed class BsdSocket
{
    public int Domain;
    public int Type;
    public int Protocol;
    public bool IsConnected;
}

/// <summary>
/// bsd:u — BSD Socket 用户服务
/// Homebrew 可用的网络套接字变体
/// </summary>
public sealed class SocketUService : IIpcService
{
    public string PortName => "bsd:u";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public SocketUService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = InitializeBsd,
            [1] = Open,
            [2] = Close,
            [3] = Connect,
            [8] = Send,
            [7] = Recv,
        };
    }

    private ResultCode InitializeBsd(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(SocketUService), "bsd:u: InitializeBsd");
        response.Data.AddRange(BitConverter.GetBytes(0x4000));
        return ResultCode.Success;
    }

    private ResultCode Open(IpcRequest request, ref IpcResponse response)
    {
        int fd = 0x20;
        response.Data.AddRange(BitConverter.GetBytes(fd));
        return ResultCode.Success;
    }

    private ResultCode Close(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    private ResultCode Connect(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    private ResultCode Send(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(request.Data.Length));
        return ResultCode.Success;
    }

    private ResultCode Recv(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    public void Dispose() { }
}
