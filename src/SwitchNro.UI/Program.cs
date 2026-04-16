using Avalonia;
using SwitchNro.Common;
using SwitchNro.Common.Configuration;
using SwitchNro.Common.Logging;

namespace SwitchNro.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 加载配置
        var config = ConfigManager.Load();
        ConfigManager.EnsureDirectories(config);

        // 设置日志级别
        if (Enum.TryParse<LogLevel>(config.Debug.LogLevel, out var logLevel))
            Logger.SetMinimumLevel(logLevel);

        Logger.Info(nameof(Program), "SwitchNro 模拟器启动");

        // 构建 Avalonia 应用
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
