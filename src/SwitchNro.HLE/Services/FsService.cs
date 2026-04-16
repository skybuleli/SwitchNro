using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// fs: 文件系统服务
/// 核心必选 - 提供 RomFS 和 SDCard 文件系统访问
/// </summary>
public sealed class FsService : IIpcService
{
    public string PortName => "fs:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public FsService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = OpenFileSystem,        // 打开文件系统
            [1] = CreateFileSystem,      // 创建文件系统
            [8] = MountContent,          // 挂载内容
            [18] = OpenSdCardFileSystem, // 打开 SD 卡文件系统
            [51] = IsSignedSystemPartitionOnSdCard,
            [100] = OpenBisFileSystem,   // 打开 Bis 文件系统
        };
    }

    private ResultCode OpenFileSystem(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FsService), "fs: OpenFileSystem");
        return ResultCode.Success;
    }

    private ResultCode CreateFileSystem(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FsService), "fs: CreateFileSystem");
        return ResultCode.Success;
    }

    private ResultCode MountContent(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FsService), "fs: MountContent");
        return ResultCode.Success;
    }

    private ResultCode OpenSdCardFileSystem(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(FsService), "fs: OpenSdCardFileSystem");
        return ResultCode.Success;
    }

    private ResultCode IsSignedSystemPartitionOnSdCard(IpcRequest request, ref IpcResponse response)
    {
        response.Data.Add(0); // false
        return ResultCode.Success;
    }

    private ResultCode OpenBisFileSystem(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(FsService), "fs: OpenBisFileSystem");
        return ResultCode.Success;
    }

    public void Dispose() { }
}
