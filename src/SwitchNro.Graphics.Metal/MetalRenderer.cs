using System;
using SwitchNro.Common.Logging;
using SwitchNro.Graphics.GAL;

namespace SwitchNro.Graphics.Metal;

/// <summary>
/// Metal 渲染后端
/// Phase 1 骨架实现 — 后续填充实际 Metal API 调用
/// </summary>
public sealed class MetalRenderer : IRenderer
{
    public string BackendName => "Metal";
    public bool IsInitialized { get; private set; }

    public void Initialize(RendererCreateInfo info)
    {
        Logger.Info(nameof(MetalRenderer), $"Metal 后端初始化: {info.Width}x{info.Height}, VSync={info.VSync}");
        // TODO: 创建 MTLDevice, MTLCommandQueue, CAMetalLayer
        IsInitialized = true;
    }

    public void SubmitCommands(IGpuCommandBuffer cmds)
    {
        // TODO: 提交 MTLCommandBuffer
    }

    public void Present()
    {
        // TODO: presentDrawable
    }

    public ITexture CreateTexture(TextureCreateInfo info)
    {
        Logger.Debug(nameof(MetalRenderer), $"创建纹理: {info.Width}x{info.Height} {info.Format}");
        return new MetalTexture(info);
    }

    public IShaderProgram CreateProgram(ShaderSource[] sources)
    {
        Logger.Debug(nameof(MetalRenderer), $"创建着色器程序: {sources.Length} 个源文件");
        return new MetalShaderProgram(sources);
    }

    public IRenderTarget CreateRenderTarget(RenderTargetCreateInfo info)
    {
        Logger.Debug(nameof(MetalRenderer), $"创建渲染目标: {info.Width}x{info.Height}");
        return new MetalRenderTarget(info);
    }

    public IntPtr GetFrameBufferHandle() => IntPtr.Zero; // TODO: 返回 CAMetalLayer 句柄

    public void Dispose()
    {
        IsInitialized = false;
        Logger.Info(nameof(MetalRenderer), "Metal 后端已释放");
    }
}

internal sealed class MetalTexture : ITexture
{
    public int Width { get; }
    public int Height { get; }
    public TextureFormat Format { get; }
    public IntPtr Handle { get; private set; }

    public MetalTexture(TextureCreateInfo info)
    {
        Width = info.Width;
        Height = info.Height;
        Format = info.Format;
    }

    public void Dispose() { }
}

internal sealed class MetalShaderProgram : IShaderProgram
{
    public IntPtr Handle { get; private set; }
    public bool IsLinked { get; private set; }

    public MetalShaderProgram(ShaderSource[] sources)
    {
        // TODO: 编译 MSL → MTLLibrary → MTLRenderPipelineState
        IsLinked = false;
    }

    public void Dispose() { }
}

internal sealed class MetalRenderTarget : IRenderTarget
{
    public int Width { get; }
    public int Height { get; }
    public IntPtr Handle { get; private set; }

    public MetalRenderTarget(RenderTargetCreateInfo info)
    {
        Width = info.Width;
        Height = info.Height;
    }

    public void Dispose() { }
}
