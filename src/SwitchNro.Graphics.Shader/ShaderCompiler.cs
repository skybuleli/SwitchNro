using System;
using SwitchNro.Common.Logging;
using SwitchNro.Graphics.GAL;

namespace SwitchNro.Graphics.Shader;

/// <summary>
/// Shader 编译器
/// Maxwell GPU Shader → IR → MSL/SPIR-V
/// Phase 1: 骨架实现
/// </summary>
public sealed class ShaderCompiler
{
    /// <summary>编译 Maxwell shader 为目标后端格式</summary>
    public CompiledShader Compile(ReadOnlySpan<byte> binary, ShaderTarget target)
    {
        Logger.Info(nameof(ShaderCompiler), $"编译 Shader: {binary.Length} 字节 → {target}");

        // TODO: Maxwell shader 反编译 → IR → 目标代码生成
        var code = target switch
        {
            ShaderTarget.MetalMsl => "// MSL placeholder\n",
            ShaderTarget.VulkanSpirv => "",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        return new CompiledShader
        {
            Code = code,
            Target = target,
            Hash = ComputeHash(binary),
        };
    }

    /// <summary>从磁盘缓存加载已编译 shader</summary>
    public static CompiledShader? LoadFromCache(string cachePath, Hash128 key)
    {
        // TODO: 从 ~/.switchnro/shader_cache/ 加载
        return null;
    }

    /// <summary>保存到磁盘缓存</summary>
    public static void SaveToCache(string cachePath, Hash128 key, CompiledShader shader)
    {
        // TODO: 保存到 ~/.switchnro/shader_cache/
    }

    private static Hash128 ComputeHash(ReadOnlySpan<byte> data)
    {
        // 简单的 FNV-1a 128-bit hash 占位
        ulong low = 0, high = 0;
        foreach (var b in data)
        {
            low = (low ^ b) * 0x100000001B3;
        }
        return new Hash128(low, high);
    }
}

/// <summary>编译后的 Shader</summary>
public sealed class CompiledShader
{
    public string Code { get; init; } = "";
    public ShaderTarget Target { get; init; }
    public Hash128 Hash { get; init; }
}

/// <summary>Shader 编译目标</summary>
public enum ShaderTarget
{
    MetalMsl,
    VulkanSpirv,
}

/// <summary>128-bit 哈希值</summary>
public readonly struct Hash128 : IEquatable<Hash128>
{
    public ulong Low { get; init; }
    public ulong High { get; init; }

    public Hash128(ulong low, ulong high)
    {
        Low = low;
        High = high;
    }

    public bool Equals(Hash128 other) => Low == other.Low && High == other.High;
    public override bool Equals(object? obj) => obj is Hash128 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Low, High);
    public override string ToString() => $"{Low:X16}{High:X16}";

    public static bool operator ==(Hash128 left, Hash128 right) => left.Equals(right);
    public static bool operator !=(Hash128 left, Hash128 right) => !left.Equals(right);
}
