using System;
using SwitchNro.Common.Logging;
using SwitchNro.Graphics.GAL;

namespace SwitchNro.Graphics.Texture;

/// <summary>
/// 纹理管理器
/// 处理纹理格式转换、ASTC 硬件/软件解码
/// </summary>
public sealed class TextureManager
{
    /// <summary>M1 是否支持 ASTC 硬件解码</summary>
    public bool IsAstcHardwareSupported { get; }

    public TextureManager()
    {
        // M1 GPU 原生支持 ASTC
        IsAstcHardwareSupported = true;
        Logger.Info(nameof(TextureManager), $"纹理管理器初始化: ASTC 硬解={IsAstcHardwareSupported}");
    }

    /// <summary>上传纹理到 GPU</summary>
    public ITexture UploadTexture(IRenderer renderer, TextureCreateInfo desc, ReadOnlySpan<byte> data)
    {
        var texture = renderer.CreateTexture(desc);

        if (IsAstcFormat(desc.Format) && IsAstcHardwareSupported)
        {
            // M1 硬件解码 — 零 CPU 开销
            Logger.Debug(nameof(TextureManager), $"ASTC 硬解纹理: {desc.Width}x{desc.Height}");
        }
        else if (IsAstcFormat(desc.Format))
        {
            // CPU 软件解码 ASTC → BCn → 上传
            Logger.Debug(nameof(TextureManager), $"ASTC 软解纹理: {desc.Width}x{desc.Height}");
            // TODO: ASTC 软解码器实现
        }

        return texture;
    }

    /// <summary>转换纹理格式</summary>
    public static ReadOnlySpan<byte> ConvertFormat(ReadOnlySpan<byte> data, TextureFormat srcFormat, TextureFormat dstFormat)
    {
        // TODO: 格式转换实现
        return data;
    }

    private static bool IsAstcFormat(TextureFormat format) =>
        format is >= TextureFormat.Astc4x4UNorm and <= TextureFormat.Astc12x12UNorm;
}
