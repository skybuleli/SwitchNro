using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;
using SwitchNro.Horizon;

namespace SwitchNro.HLE.Services;

/// <summary>
/// nvdrv:a / nvservices — NVIDIA 驱动服务
/// 核心图形服务 - 提供GPU驱动接口、显示控制、内存管理
/// Homebrew 通过此服务与 GPU 交互 (ioctl)
/// </summary>
public sealed class NvService : IIpcService
{
    private readonly string _portName;
    private readonly IpcServiceManager? _serviceManager;
    public string PortName => _portName;

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>NV 设备文件描述符表</summary>
    private readonly Dictionary<int, string> _fdTable = new();
    private int _nextFd = 0x10;

    public NvService(string portName = "nvdrv:a", IpcServiceManager? serviceManager = null)
    {
        _portName = portName;
        _serviceManager = serviceManager;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = Open,                    // 打开 NV 设备
            [1] = Ioctl,                  // 发送 Ioctl 命令
            [2] = Close,                  // 关闭 NV 设备
            [3] = QueryEvent,             // 查询事件
            [4] = MapMemory,              // 映射 NV 内存
            [5] = GetStatus,              // 获取驱动状态
            [6] = SetSubmitTimeout,       // 设置提交超时
            [8] = Ioctl2,                 // Ioctl 扩展 (额外输入缓冲区)
            [9] = Ioctl3,                 // Ioctl 扩展 (额外输出缓冲区)
        };
    }

    /// <summary>命令 0: Open — 打开 NV 设备文件</summary>
    private ResultCode Open(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length == 0)
            return ResultCode.SfResult(3); // Invalid argument

        var devicePath = System.Text.Encoding.ASCII.GetString(request.Data).TrimEnd('\0');
        Logger.Info(nameof(NvService), $"nvdrv: Open(\"{devicePath}\")");

        int fd = _nextFd++;
        _fdTable[fd] = devicePath;

        // 返回文件描述符
        response.Data.AddRange(BitConverter.GetBytes(fd));
        return ResultCode.Success;
    }

    /// <summary>命令 1: Ioctl — 发送 Ioctl 命令到 NV 设备</summary>
    private ResultCode Ioctl(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4)
            return ResultCode.SfResult(3); // Invalid argument

        uint ioctlCmd = BitConverter.ToUInt32(request.Data, 0);
        Logger.Debug(nameof(NvService), $"nvdrv: Ioctl(0x{ioctlCmd:X8})");

        // 常见 NV ioctl 处理
        return HandleNvIoctl(ioctlCmd, ref response);
    }

    /// <summary>命令 2: Close — 关闭 NV 设备文件描述符</summary>
    private ResultCode Close(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length >= 4)
        {
            int fd = BitConverter.ToInt32(request.Data, 0);
            _fdTable.Remove(fd);
            Logger.Debug(nameof(NvService), $"nvdrv: Close(fd={fd})");
        }
        return ResultCode.Success;
    }

    /// <summary>命令 3: QueryEvent — 查询 NV 事件</summary>
    private ResultCode QueryEvent(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvService), "nvdrv: QueryEvent");
        // 通过 HandleTable 创建真实 KReadableEvent 句柄
        if (_serviceManager?.HandleTable != null)
        {
            int handle = _serviceManager.HandleTable.CreateHandle(new KReadableEvent());
            response.CopyHandles.Add(handle);
        }
        else
        {
            Logger.Warning(nameof(NvService), "QueryEvent: HandleTable 未设置，返回虚拟句柄");
            response.CopyHandles.Add(0x200);
        }
        return ResultCode.Success;
    }

    /// <summary>命令 4: MapMemory — 映射 NV 内存区域</summary>
    private ResultCode MapMemory(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvService), "nvdrv: MapMemory");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetStatus — 获取驱动状态</summary>
    private ResultCode GetStatus(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvService), "nvdrv: GetStatus");
        // 返回初始化完成状态 (0 = 成功)
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 6: SetSubmitTimeout — 设置 GPU 提交超时</summary>
    private ResultCode SetSubmitTimeout(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvService), "nvdrv: SetSubmitTimeout");
        return ResultCode.Success;
    }

    /// <summary>命令 8: Ioctl2 — 扩展 Ioctl (额外输入)</summary>
    private ResultCode Ioctl2(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvService), "nvdrv: Ioctl2");
        return ResultCode.Success;
    }

    /// <summary>命令 9: Ioctl3 — 扩展 Ioctl (额外输出)</summary>
    private ResultCode Ioctl3(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvService), "nvdrv: Ioctl3");
        return ResultCode.Success;
    }

    /// <summary>处理 NV ioctl 子命令</summary>
    private static ResultCode HandleNvIoctl(uint cmd, ref IpcResponse response)
    {
        // NV ioctl 高 8 位表示类别
        uint category = (cmd >> 24) & 0xFF;

        return category switch
        {
            0x00 => HandleCtrlIoctl(cmd, ref response),    // NV_HOST_IOCTL_CTRL
            0x01 => HandleChannelIoctl(cmd, ref response), // NV_HOST_IOCTL_CHANNEL
            0x02 => HandleDeviceIoctl(cmd, ref response),  // NV_HOST_IOCTL_DEVICE
            _ => HandleUnknownIoctl(cmd, ref response),
        };
    }

    private static ResultCode HandleCtrlIoctl(uint cmd, ref IpcResponse response)
    {
        uint function = (cmd >> 16) & 0xFF;
        Logger.Debug(nameof(NvService), $"  NV_CTRL ioctl: func={function}");

        // NV_IOCTL_CTRL_SYNCPT_ALLOC 等
        return ResultCode.Success;
    }

    private static ResultCode HandleChannelIoctl(uint cmd, ref IpcResponse response)
    {
        uint function = (cmd >> 16) & 0xFF;
        Logger.Debug(nameof(NvService), $"  NV_CHANNEL ioctl: func={function}");

        // NV_IOCTL_CHANNEL_SUBMIT 等
        return ResultCode.Success;
    }

    private static ResultCode HandleDeviceIoctl(uint cmd, ref IpcResponse response)
    {
        uint function = (cmd >> 16) & 0xFF;
        Logger.Debug(nameof(NvService), $"  NV_DEVICE ioctl: func={function}");
        return ResultCode.Success;
    }

    private static ResultCode HandleUnknownIoctl(uint cmd, ref IpcResponse response)
    {
        Logger.Info(nameof(NvService), $"  NV ioctl (Stub Success): 0x{cmd:X8}");
        
        // 为 MVP 简化：对大部分 ioctl 返回成功且填充全零数据（如果需要）
        // 很多 libnx 特性探测只要不返回错误码就会继续
        response.Data.AddRange(new byte[64]); // 填充一些空数据防止溢出
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// nvmemp: — NVIDIA 内存引脚服务
/// 管理显存的引脚/取消引脚操作
/// </summary>
public sealed class NvMemPService : IIpcService
{
    public string PortName => "nvmemp:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public NvMemPService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = Open,       // 打开
            [1] = Ioctl,     // Ioctl
            [2] = Close,     // 关闭
        };
    }

    private ResultCode Open(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvMemPService), "nvmemp: Open");
        return ResultCode.Success;
    }

    private ResultCode Ioctl(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvMemPService), "nvmemp: Ioctl");
        return ResultCode.Success;
    }

    private ResultCode Close(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(NvMemPService), "nvmemp: Close");
        return ResultCode.Success;
    }

    public void Dispose() { }
}
