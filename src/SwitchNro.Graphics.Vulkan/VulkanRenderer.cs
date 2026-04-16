using System;
using SwitchNro.Common.Logging;
using SwitchNro.Graphics.GAL;

namespace SwitchNro.Graphics.Vulkan;

/// <summary>
/// Vulkan (MoltenVK) 渲染后端
/// 作为 Metal 不可用时的兼容性回退
/// </summary>
public sealed class VulkanRenderer : IRenderer
{
    public string BackendName => "Vulkan (MoltenVK)";
    public bool IsInitialized { get; private set; }

    public void Initialize(RendererCreateInfo info)
    {
        Logger.Info(nameof(VulkanRenderer), $"Vulkan/MoltenVK 后端初始化: {info.Width}x{info.Height}");
        // TODO: 创建 VkInstance, VkDevice, VkSwapchain
        IsInitialized = true;
    }

    public void SubmitCommands(IGpuCommandBuffer cmds) { }
    public void Present() { }
    public ITexture CreateTexture(TextureCreateInfo info) => throw new NotImplementedException();
    public IShaderProgram CreateProgram(ShaderSource[] sources) => throw new NotImplementedException();
    public IRenderTarget CreateRenderTarget(RenderTargetCreateInfo info) => throw new NotImplementedException();
    public IntPtr GetFrameBufferHandle() => IntPtr.Zero;

    public void Dispose()
    {
        IsInitialized = false;
    }
}
