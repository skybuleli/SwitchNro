using System.Text.Json.Serialization;

namespace SwitchNro.Common.Configuration;

/// <summary>模拟器全局配置</summary>
public sealed class EmulatorConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>图形配置</summary>
    public GraphicsConfig Graphics { get; set; } = new();

    /// <summary>音频配置</summary>
    public AudioConfig Audio { get; set; } = new();

    /// <summary>输入配置</summary>
    public InputConfig Input { get; set; } = new();

    /// <summary>文件系统配置</summary>
    public FileSystemConfig FileSystem { get; set; } = new();

    /// <summary>调试配置</summary>
    public DebugConfig Debug { get; set; } = new();

    /// <summary>内存配置</summary>
    public MemoryConfig Memory { get; set; } = new();
}

public sealed class GraphicsConfig
{
    public string Backend { get; set; } = "Metal";
    public bool VSync { get; set; } = true;
    public bool AstcHardwareDecode { get; set; } = true;
    public string ShaderCachePath { get; set; } = "~/.switchnro/shader_cache";
}

public sealed class AudioConfig
{
    public string Backend { get; set; } = "SDL2";
    public float Volume { get; set; } = 1.0f;
    public int LatencyMs { get; set; } = 50;
}

public sealed class InputConfig
{
    public Dictionary<string, string> KeyboardMapping { get; set; } = new()
    {
        ["A"] = "X", ["B"] = "Z", ["X"] = "S", ["Y"] = "A",
        ["L"] = "Q", ["R"] = "E", ["ZL"] = "1", ["ZR"] = "3",
        ["Plus"] = "Enter", ["Minus"] = "Backspace",
        ["DUp"] = "Up", ["DDown"] = "Down", ["DLeft"] = "Left", ["DRight"] = "Right",
        ["LStick"] = "WASD", ["RStick"] = "IJKL",
        ["Home"] = "Escape", ["Screenshot"] = "F12",
    };
    public bool MouseAsTouch { get; set; } = true;
    public bool BluetoothGamepad { get; set; } = true;
}

public sealed class FileSystemConfig
{
    public string SdCardPath { get; set; } = "~/.switchnro/sdcard";
    public string SaveDataPath { get; set; } = "~/.switchnro/save";
}

public sealed class DebugConfig
{
    public string LogLevel { get; set; } = "Info";
    public bool SvcLogging { get; set; }
    public bool IpcLogging { get; set; }
}

public sealed class MemoryConfig
{
    public double MaxResidentGb { get; set; } = 3.5;
    public bool EnablePageReclaim { get; set; } = true;
}
