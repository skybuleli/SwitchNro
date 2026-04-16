using System;
using System.IO;
using System.Text.Json;

namespace SwitchNro.Common.Configuration;

/// <summary>配置管理器 - 从磁盘加载/保存 JSON 配置</summary>
public sealed class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".switchnro");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>加载配置，如不存在则创建默认配置</summary>
    public static EmulatorConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new EmulatorConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<EmulatorConfig>(json, s_jsonOptions) ?? new EmulatorConfig();
    }

    /// <summary>保存配置到磁盘</summary>
    public static void Save(EmulatorConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, s_jsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>确保工作目录结构存在</summary>
    public static void EnsureDirectories(EmulatorConfig config)
    {
        var dirs = new[]
        {
            config.FileSystem.SdCardPath,
            config.FileSystem.SaveDataPath,
            config.Graphics.ShaderCachePath,
        };

        foreach (var dir in dirs)
        {
            var expanded = ExpandPath(dir);
            if (!Directory.Exists(expanded))
                Directory.CreateDirectory(expanded);
        }
    }

    /// <summary>展开 ~ 为用户主目录</summary>
    public static string ExpandPath(string path) =>
        path.StartsWith('~') ? path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.Ordinal) : path;
}
