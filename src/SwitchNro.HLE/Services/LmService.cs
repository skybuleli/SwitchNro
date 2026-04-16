using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// lm — 日志管理服务 (ILogService)
/// 提供日志记录器创建接口，Guest 进程通过此服务获取 ILogger 实例
/// 命令表基于 SwitchBrew Log_services 页面
/// </summary>
public sealed class LmService : IIpcService
{
    public string PortName => "lm";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>共享的 ILogger 实例（与 lm:get 服务共用同一实例）</summary>
    private readonly LmLoggerService _logger;

    /// <summary>下一个虚拟 Logger 句柄</summary>
    private uint _nextLoggerHandle = 0xF0000000;

    /// <summary>已打开的 Logger 句柄映射</summary>
    private readonly Dictionary<int, LmLoggerService> _loggerHandles = new();

    public LmService(LmLoggerService logger)
    {
        _logger = logger;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = OpenLogger,       // 打开日志记录器
        };
    }

    /// <summary>命令 0: OpenLogger — 打开指定进程的日志记录器</summary>
    private ResultCode OpenLogger(IpcRequest request, ref IpcResponse response)
    {
        ulong processId = 0;
        if (request.Data.Length >= 8)
            processId = BitConverter.ToUInt64(request.Data, 0);

        int handle = unchecked((int)_nextLoggerHandle++);
        _loggerHandles[handle] = _logger;

        Logger.Info(nameof(LmService), $"lm: OpenLogger(ProcessId=0x{processId:X16}) → handle=0x{handle:X8}");

        // 返回 ILogger 对象句柄
        response.Data.AddRange(BitConverter.GetBytes(handle));
        return ResultCode.Success;
    }

    /// <summary>获取指定句柄的 Logger 实例（供命令分发器使用）</summary>
    internal LmLoggerService? GetLogger(int handle) =>
        _loggerHandles.TryGetValue(handle, out var logger) ? logger : null;

    public void Dispose()
    {
        _loggerHandles.Clear();
    }
}

/// <summary>
/// ILogger — 日志记录器接口 (nn::lm::ILogger)
/// 提供日志写入、目标设置等功能
/// 在实际 IPC 中作为独立 session 存在，此处内联到 LmService 中
/// </summary>
public sealed class LmLoggerService : IIpcService
{
    public string PortName => "ilm"; // 内部虚拟端口名 — ILogger 通过 lm.OpenLogger 获取，不注册为命名端口

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>日志目标掩码 (1=TMA, 2=UART, 4=UART_sleeping, 0xFFFF=All)</summary>
    private uint _destinationMask = 0xFFFF; // 默认全部目标

    /// <summary>内部日志缓冲区（存储最近的日志消息）</summary>
    private readonly List<string> _logBuffer = new();

    /// <summary>最大日志缓冲区条目数</summary>
    private const int MaxLogEntries = 256;

    /// <summary>日志丢弃计数（缓冲区满时递增）</summary>
    private ulong _dropCount;

    /// <summary>是否正在记录（由 lm:get 的 StartLogging/StopLogging 控制）</summary>
    private bool _isLogging;

    /// <summary>外部可查询日志目标</summary>
    public uint DestinationMask => _destinationMask;

    /// <summary>外部可查询日志缓冲区</summary>
    public IReadOnlyList<string> LogBuffer => _logBuffer;

    /// <summary>外部可查询日志丢弃计数</summary>
    public ulong DropCount => _dropCount;

    /// <summary>外部可设置/查询日志记录状态</summary>
    public bool IsLogging
    {
        get => _isLogging;
        set => _isLogging = value;
    }

    public LmLoggerService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = Log,                  // 写入日志
            [1] = SetDestination,       // [3.0.0+] 设置日志目标
            [2] = TransmitHashedLog,     // [20.0.0+] 传输哈希日志
        };
    }

    /// <summary>命令 0: Log — 写入日志消息</summary>
    private ResultCode Log(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length > 0)
        {
            // 从请求数据中提取文本日志
            string message = Encoding.UTF8.GetString(request.Data).TrimEnd('\0');

            if (_logBuffer.Count >= MaxLogEntries)
            {
                _logBuffer.RemoveAt(0);
                _dropCount++;
            }
            _logBuffer.Add(message);

            Logger.Debug(nameof(LmLoggerService), $"ILogger: Log(\"{message}\")");
        }

        // ILogger.Log 总是返回 Success
        return ResultCode.Success;
    }

    /// <summary>命令 1: SetDestination — [3.0.0+] 设置日志输出目标</summary>
    private ResultCode SetDestination(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4)
            return ResultCode.LmResult(2); // Invalid size

        _destinationMask = BitConverter.ToUInt32(request.Data, 0);
        Logger.Info(nameof(LmLoggerService), $"ILogger: SetDestination(mask=0x{_destinationMask:X8})");
        return ResultCode.Success;
    }

    /// <summary>命令 2: TransmitHashedLog — [20.0.0+] 传输哈希日志（stub）</summary>
    private ResultCode TransmitHashedLog(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(LmLoggerService), "ILogger: TransmitHashedLog (stub)");
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// lm:get — 日志获取服务 (ILogGetter)
/// 提供日志收集启停和获取接口
/// 注意：此服务在 retail 固件上不存在，仅供开发/调试使用
/// </summary>
public sealed class LmGetService : IIpcService
{
    public string PortName => "lm:get";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>共享的 Logger 实例引用</summary>
    private readonly LmLoggerService _logger;

    public LmGetService(LmLoggerService logger)
    {
        _logger = logger;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = StartLogging,             // 开始日志收集
            [1] = StopLogging,              // 停止日志收集
            [2] = GetLog,                   // 获取日志数据
        };
    }

    /// <summary>命令 0: StartLogging — 开始日志收集</summary>
    private ResultCode StartLogging(IpcRequest request, ref IpcResponse response)
    {
        _logger.IsLogging = true;
        Logger.Debug(nameof(LmGetService), "lm:get: StartLogging");
        return ResultCode.Success;
    }

    /// <summary>命令 1: StopLogging — 停止日志收集</summary>
    private ResultCode StopLogging(IpcRequest request, ref IpcResponse response)
    {
        _logger.IsLogging = false;
        Logger.Debug(nameof(LmGetService), "lm:get: StopLogging");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetLog — 获取日志数据</summary>
    private ResultCode GetLog(IpcRequest request, ref IpcResponse response)
    {
        // 输出: u64 readSize + u64 packetDropCount
        if (_logger.LogBuffer.Count > 0 && _logger.IsLogging)
        {
            // 拼接所有日志条目
            var sb = new StringBuilder();
            foreach (var entry in _logger.LogBuffer)
                sb.AppendLine(entry);

            byte[] logData = Encoding.UTF8.GetBytes(sb.ToString());
            response.Data.AddRange(logData);
        }

        // 返回元数据: readSize + dropCount
        response.Data.AddRange(BitConverter.GetBytes((ulong)response.Data.Count));
        response.Data.AddRange(BitConverter.GetBytes(_logger.DropCount));

        Logger.Debug(nameof(LmGetService), $"lm:get: GetLog → {_logger.LogBuffer.Count} entries, {_logger.DropCount} drops");
        return ResultCode.Success;
    }

    public void Dispose() { }
}
