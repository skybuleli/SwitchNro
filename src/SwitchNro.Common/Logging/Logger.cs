using System;

namespace SwitchNro.Common.Logging;

/// <summary>全局日志管理器</summary>
public static class Logger
{
    private static LogLevel _minimumLevel = LogLevel.Info;

    /// <summary>设置最低日志级别</summary>
    public static void SetMinimumLevel(LogLevel level) => _minimumLevel = level;

    /// <summary>记录日志</summary>
    public static void Log(LogLevel level, string source, string message)
    {
        if (level < _minimumLevel) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        var levelStr = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Fatal => "FTL",
            _ => "???"
        };

        Console.WriteLine($"[{timestamp}] [{levelStr}] [{source}] {message}");
    }

    public static void Trace(string source, string message) => Log(LogLevel.Trace, source, message);
    public static void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
    public static void Info(string source, string message) => Log(LogLevel.Info, source, message);
    public static void Warning(string source, string message) => Log(LogLevel.Warning, source, message);
    public static void Error(string source, string message) => Log(LogLevel.Error, source, message);
    public static void Fatal(string source, string message) => Log(LogLevel.Fatal, source, message);
}
