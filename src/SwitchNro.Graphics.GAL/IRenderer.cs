using System;

namespace SwitchNro.Graphics.GAL;

/// <summary>
/// 图形抽象层 - 渲染器核心接口
/// 所有图形后端（Metal/Vulkan）实现此接口
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>初始化渲染器</summary>
    void Initialize(RendererCreateInfo info);

    /// <summary>提交 GPU 命令缓冲区</summary>
    void SubmitCommands(IGpuCommandBuffer cmds);

    /// <summary>呈现帧到目标表面</summary>
    void Present();

    /// <summary>创建纹理</summary>
    ITexture CreateTexture(TextureCreateInfo info);

    /// <summary>创建着色器程序</summary>
    IShaderProgram CreateProgram(ShaderSource[] sources);

    /// <summary>创建渲染目标</summary>
    IRenderTarget CreateRenderTarget(RenderTargetCreateInfo info);

    /// <summary>获取帧缓冲句柄（用于嵌入 Avalonia）</summary>
    IntPtr GetFrameBufferHandle();

    /// <summary>当前后端名称</summary>
    string BackendName { get; }

    /// <summary>是否已初始化</summary>
    bool IsInitialized { get; }
}

/// <summary>渲染器创建信息</summary>
public sealed class RendererCreateInfo
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool VSync { get; init; } = true;
}

/// <summary>GPU 命令缓冲区接口</summary>
public interface IGpuCommandBuffer : IDisposable
{
    void Begin();
    void Finish();
    void DrawArrays(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0);
    void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0, int vertexOffset = 0, int firstInstance = 0);
    void SetViewport(int x, int y, int width, int height);
    void SetScissor(int x, int y, int width, int height);
    void BindTexture(int binding, ITexture texture);
    void BindShaderProgram(IShaderProgram program);
    void BindRenderTarget(IRenderTarget target);
    void ClearColor(float r, float g, float b, float a);
    void UploadTexture(ITexture texture, ReadOnlySpan<byte> data);
}

/// <summary>纹理接口</summary>
public interface ITexture : IDisposable
{
    int Width { get; }
    int Height { get; }
    TextureFormat Format { get; }
    IntPtr Handle { get; }
}

/// <summary>着色器程序接口</summary>
public interface IShaderProgram : IDisposable
{
    IntPtr Handle { get; }
    bool IsLinked { get; }
}

/// <summary>渲染目标接口</summary>
public interface IRenderTarget : IDisposable
{
    int Width { get; }
    int Height { get; }
    IntPtr Handle { get; }
}

/// <summary>纹理创建信息</summary>
public sealed class TextureCreateInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TextureFormat Format { get; init; } = TextureFormat.R8G8B8A8UNorm;
    public TextureUsage Usage { get; init; } = TextureUsage.ShaderRead;
    public int MipLevels { get; init; } = 1;
    public int ArrayLayers { get; init; } = 1;
}

/// <summary>渲染目标创建信息</summary>
public sealed class RenderTargetCreateInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TextureFormat Format { get; init; } = TextureFormat.B8G8R8A8UNorm;
}

/// <summary>Shader 源码</summary>
public sealed class ShaderSource
{
    public string Code { get; init; } = "";
    public ShaderStage Stage { get; init; }
    public ShaderLanguage Language { get; init; }
}

/// <summary>纹理格式</summary>
public enum TextureFormat
{
    R8G8B8A8UNorm,
    B8G8R8A8UNorm,
    R5G6B5UNorm,
    R4G4B4A4UNorm,
    R8UNorm,
    R8G8UNorm,
    R16SFloat,
    R16G16SFloat,
    R16G16B16A16SFloat,
    R32SFloat,
    R32G32SFloat,
    R32G32B32A32SFloat,
    D16UNorm,
    D24UNormS8UInt,
    D32SFloatS8UInt,
    BC1RgbaUNorm,
    BC2UNorm,
    BC3UNorm,
    BC4UNorm,
    BC5UNorm,
    BC7UNorm,
    Astc4x4UNorm,
    Astc5x4UNorm,
    Astc5x5UNorm,
    Astc6x6UNorm,
    Astc8x5UNorm,
    Astc8x6UNorm,
    Astc8x8UNorm,
    Astc10x5UNorm,
    Astc10x8UNorm,
    Astc10x10UNorm,
    Astc12x10UNorm,
    Astc12x12UNorm,
}

/// <summary>纹理用途</summary>
[Flags]
public enum TextureUsage
{
    None = 0,
    ShaderRead = 1 << 0,
    ShaderWrite = 1 << 1,
    RenderTarget = 1 << 2,
    DepthStencil = 1 << 3,
    Upload = 1 << 4,
}

/// <summary>Shader 阶段</summary>
public enum ShaderStage
{
    Vertex,
    TessellationControl,
    TessellationEvaluation,
    Geometry,
    Fragment,
    Compute,
}

/// <summary>Shader 语言</summary>
public enum ShaderLanguage
{
    Msl,       // Metal Shading Language
    Spirv,     // Vulkan SPIR-V
    Glsl,      // OpenGL GLSL
}
