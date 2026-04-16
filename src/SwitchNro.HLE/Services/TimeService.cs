using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>共享时间服务辅助方法</summary>
internal static class TimeHelper
{
    /// <summary>获取系统时钟类型 → NetworkClock (1)</summary>
    public static ResultCode GetSystemClockType(IpcRequest _, ref IpcResponse response)
    {
        response.Data.Add(1);
        return ResultCode.Success;
    }

    /// <summary>获取高精度稳定时钟 (ticks + clockSourceId)</summary>
    public static ResultCode GetStandardSteadyClock(IpcRequest _, ref IpcResponse response)
    {
        var ticks = (ulong)DateTimeOffset.UtcNow.UtcTicks;
        response.Data.AddRange(BitConverter.GetBytes(ticks));
        response.Data.AddRange(BitConverter.GetBytes(0UL)); // clockSourceId (uint64)
        return ResultCode.Success;
    }

    /// <summary>获取用户本地时间 (POSIX seconds)</summary>
    public static ResultCode GetStandardUserSystemClock(IpcRequest _, ref IpcResponse response)
    {
        var posixTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        response.Data.AddRange(BitConverter.GetBytes(posixTime));
        return ResultCode.Success;
    }
}

/// <summary>
/// time:s — 系统时间服务 (System Time)
/// 核心必选 - 提供系统时钟、时区、网络时间功能
/// Homebrew 通常通过此服务获取当前时间
/// </summary>
public sealed class TimeService : IIpcService
{
    public string PortName => "time:s";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public TimeService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = TimeHelper.GetSystemClockType,
            [1]  = TimeHelper.GetStandardSteadyClock,
            [2]  = GetSteadyClockCore,
            [3]  = TimeHelper.GetStandardUserSystemClock,
            [4]  = GetStandardNetworkSystemClock,
            [5]  = GetStandardUserSystemClockCore,
            [6]  = GetStandardNetworkSystemClockCore,
            [7]  = GetTimeZoneService,
            [50] = SetStandardSteadyClockBaseTime,
        };
    }

    /// <summary>命令 2: GetSteadyClockCore</summary>
    private static ResultCode GetSteadyClockCore(IpcRequest request, ref IpcResponse response)
    {
        TimeHelper.GetStandardSteadyClock(request, ref response);
        response.Data.Add(0); // isStandardSteadyClock = false
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetStandardNetworkSystemClock</summary>
    private static ResultCode GetStandardNetworkSystemClock(IpcRequest request, ref IpcResponse response)
    {
        return TimeHelper.GetStandardUserSystemClock(request, ref response);
    }

    /// <summary>命令 5: GetStandardUserSystemClockCore</summary>
    private static ResultCode GetStandardUserSystemClockCore(IpcRequest request, ref IpcResponse response)
    {
        return TimeHelper.GetStandardUserSystemClock(request, ref response);
    }

    /// <summary>命令 6: GetStandardNetworkSystemClockCore</summary>
    private static ResultCode GetStandardNetworkSystemClockCore(IpcRequest request, ref IpcResponse response)
    {
        return TimeHelper.GetStandardUserSystemClock(request, ref response);
    }

    /// <summary>命令 7: GetTimeZoneService — 获取时区服务</summary>
    private static ResultCode GetTimeZoneService(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(TimeService), "time:s: GetTimeZoneService");
        return ResultCode.Success;
    }

    /// <summary>命令 50: SetStandardSteadyClockBaseTime</summary>
    private static ResultCode SetStandardSteadyClockBaseTime(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(TimeService), "time:s: SetStandardSteadyClockBaseTime");
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// time:a — 时间管理服务 (管理员)
/// 仅限系统进程使用
/// </summary>
public sealed class TimeAService : IIpcService
{
    public string PortName => "time:a";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public TimeAService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = TimeHelper.GetSystemClockType,
            [1] = TimeHelper.GetStandardSteadyClock,
            [3] = TimeHelper.GetStandardUserSystemClock,
        };
    }

    public void Dispose() { }
}

/// <summary>
/// time:u — 用户时间服务
/// Homebrew 可用的时间服务变体
/// </summary>
public sealed class TimeUService : IIpcService
{
    public string PortName => "time:u";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public TimeUService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = TimeHelper.GetSystemClockType,
            [1] = TimeHelper.GetStandardSteadyClock,
            [3] = TimeHelper.GetStandardUserSystemClock,
        };
    }

    public void Dispose() { }
}
