using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SwitchNro.Common.Utilities;

/// <summary>高性能 Span 辅助方法</summary>
public static class SpanHelper
{
    /// <summary>将 Span 以 Little Endian 读取为指定整数类型</summary>
    public static T ReadLittleEndian<T>(ReadOnlySpan<byte> span) where T : unmanaged
    {
        if (span.Length < Unsafe.SizeOf<T>())
            throw new ArgumentOutOfRangeException(nameof(span), $"Span 长度不足，需要 {Unsafe.SizeOf<T>()} 字节");

        return Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in MemoryMarshal.GetReference(span)));
    }

    /// <summary>将整数以 Little Endian 写入 Span</summary>
    public static void WriteLittleEndian<T>(Span<byte> span, T value) where T : unmanaged
    {
        if (span.Length < Unsafe.SizeOf<T>())
            throw new ArgumentOutOfRangeException(nameof(span), $"Span 长度不足，需要 {Unsafe.SizeOf<T>()} 字节");

        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value);
    }

    /// <summary>从流的指定位置读取结构体</summary>
    public static T ReadStruct<T>(Stream stream, long offset) where T : unmanaged
    {
        stream.Position = offset;
        Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
        stream.ReadExactly(buffer);
        return ReadLittleEndian<T>(buffer);
    }
}
