using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 Audio Renderer 服务状态
/// </summary>
public sealed class AudRenState
{
    /// <summary>默认采样率 (48kHz)</summary>
    private const uint DefaultSampleRate = 48000;

    /// <summary>每帧采样数 (48kHz@60fps = 960 samples/frame)</summary>
    private const uint DefaultSampleCount = 960;

    /// <summary>混音缓冲区数量</summary>
    private const uint DefaultMixBufferCount = 1;

    /// <summary>渲染器是否已启动</summary>
    private bool _isStarted;

    /// <summary>渲染时间限制 (毫秒)</summary>
    private uint _renderingTimeLimit = 5; // 默认 5ms

    /// <summary>渲染器是否已启动</summary>
    public bool IsStarted
    {
        get => _isStarted;
        set => _isStarted = value;
    }

    /// <summary>渲染时间限制</summary>
    public uint RenderingTimeLimit
    {
        get => _renderingTimeLimit;
        set => _renderingTimeLimit = value;
    }

    /// <summary>默认采样率</summary>
    public static uint SampleRate => DefaultSampleRate;

    /// <summary>每帧采样数</summary>
    public static uint SampleCount => DefaultSampleCount;

    /// <summary>混音缓冲区数量</summary>
    public static uint MixBufferCount => DefaultMixBufferCount;

    /// <summary>计算工作缓冲区大小 (基于 AudioRendererParameter)</summary>
    /// <remarks>
    /// 简化计算: 真实 AudioRendererParameter 结构包含 20+ 字段 (revision, behavior flags,
    /// voice drop enable 等)，这里仅解析核心 7 个字段。对简单 homebrew 足够，复杂应用
    /// 发送完整参数时数据可能被错误偏移读取。真实值约 0x16C000 ~ 0x400000 字节。
    /// </remarks>
    public static ulong CalculateWorkBufferSize(uint sampleRate, uint sampleCount, uint mixBufferCount,
        uint voiceCount, uint effectCount, uint sinkCount, uint subMixCount)
    {
        // 基于 Ryujinx 的最小工作缓冲区估算
        ulong size = 0x4000; // 基础开销
        size += (ulong)mixBufferCount * sampleCount * 4; // mix buffers (32-bit float)
        size += (ulong)voiceCount * 0x100; // voice 状态
        size += (ulong)effectCount * 0x80; // effect 状态
        size += (ulong)sinkCount * 0x100; // sink 状态
        size += (ulong)subMixCount * 0x200; // sub-mix 状态
        // 按 0x1000 对齐向上取整
        size = (size + 0xFFF) & ~0xFFFUL;
        // 保证最小 0x170000
        return Math.Max(size, 0x170000);
    }
}

/// <summary>
/// audren:u / audren:u2 — 音频渲染器管理服务 (IAudioRendererManager)
/// nn::audio::detail::IAudioRendererManager
/// 90% 的 homebrew 游戏通过此服务渲染音频（而非 audout:u 低级输出）
/// 提供音频渲染器实例的创建和系统参数查询
/// 命令表基于 SwitchBrew Audio_services 页面和 Ryujinx 实现
/// </summary>
public abstract class AudRenManagerBase : IIpcService
{
    public abstract string PortName { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly AudRenState _state;

    protected AudRenManagerBase(AudRenState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = OpenAudioRenderer,                         // 打开 IAudioRenderer
            [1]  = GetWorkBufferSize,                         // 获取工作缓冲区大小
            [2]  = GetAudioRendererSampleRate,                // 获取采样率
            [3]  = GetAudioRendererSampleCount,               // 获取每帧采样数
            [4]  = GetAudioRendererMixBufferCount,            // 获取混音缓冲区数量
            [5]  = GetAudioRendererPerformanceMetricsSampleRate, // 获取性能指标采样率
            [6]  = GetAudioDeviceService,                    // 获取 IAudioDevice
            [7]  = OpenAudioRenderer2,                        // [2.0.0+] 打开 IAudioRenderer (v2)
            [8]  = GetAudioDeviceService2,                    // [2.0.0+] 获取 IAudioDevice (v2)
            [9]  = GetAudioDeviceServiceWithRevisionInfo,     // [4.0.0+] 带版本号获取 IAudioDevice
            [10] = GetAudioDeviceServiceWithRevisionInfo2,    // [4.0.0+] 带版本号获取 IAudioDevice (v2)
        };
    }

    /// <summary>命令 0: OpenAudioRenderer — 打开 IAudioRenderer</summary>
    private ResultCode OpenAudioRenderer(IpcRequest request, ref IpcResponse response)
    {
        int rendererHandle = unchecked((int)0xFFFF1000);
        response.Data.AddRange(BitConverter.GetBytes(rendererHandle));
        Logger.Info(PortName, $"{PortName}: OpenAudioRenderer → IAudioRenderer handle");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetWorkBufferSize — 获取工作缓冲区大小</summary>
    private ResultCode GetWorkBufferSize(IpcRequest request, ref IpcResponse response)
    {
        // 解析 AudioRendererParameter (简化: 使用默认值)
        uint sampleRate = AudRenState.SampleRate;
        uint sampleCount = AudRenState.SampleCount;
        uint mixBufferCount = AudRenState.MixBufferCount;
        uint voiceCount = 32;   // 默认最大 voice 数
        uint effectCount = 16;  // 默认最大 effect 数
        uint sinkCount = 16;    // 默认最大 sink 数
        uint subMixCount = 8;   // 默认最大 sub-mix 数

        // 尝试从请求中读取参数 (如果有的话)
        if (request.Data.Length >= 28)
        {
            sampleRate = BitConverter.ToUInt32(request.Data, 0);
            sampleCount = BitConverter.ToUInt32(request.Data, 4);
            mixBufferCount = BitConverter.ToUInt32(request.Data, 8);
            voiceCount = Math.Max(BitConverter.ToUInt32(request.Data, 12), 1);
            effectCount = BitConverter.ToUInt32(request.Data, 16);
            sinkCount = BitConverter.ToUInt32(request.Data, 20);
            subMixCount = BitConverter.ToUInt32(request.Data, 24);
        }

        ulong workBufferSize = AudRenState.CalculateWorkBufferSize(
            sampleRate, sampleCount, mixBufferCount, voiceCount, effectCount, sinkCount, subMixCount);
        response.Data.AddRange(BitConverter.GetBytes(workBufferSize));
        Logger.Debug(PortName, $"{PortName}: GetWorkBufferSize → 0x{workBufferSize:X16}");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetAudioRendererSampleRate — 获取采样率</summary>
    private ResultCode GetAudioRendererSampleRate(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(AudRenState.SampleRate));
        Logger.Debug(PortName, $"{PortName}: GetAudioRendererSampleRate → {AudRenState.SampleRate}");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetAudioRendererSampleCount — 获取每帧采样数</summary>
    private ResultCode GetAudioRendererSampleCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(AudRenState.SampleCount));
        Logger.Debug(PortName, $"{PortName}: GetAudioRendererSampleCount → {AudRenState.SampleCount}");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetAudioRendererMixBufferCount — 获取混音缓冲区数量</summary>
    private ResultCode GetAudioRendererMixBufferCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(AudRenState.MixBufferCount));
        Logger.Debug(PortName, $"{PortName}: GetAudioRendererMixBufferCount → {AudRenState.MixBufferCount}");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetAudioRendererPerformanceMetricsSampleRate — 获取性能指标采样率</summary>
    private ResultCode GetAudioRendererPerformanceMetricsSampleRate(IpcRequest request, ref IpcResponse response)
    {
        // 返回 0 表示性能指标不可用 (非开发机)
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(PortName, $"{PortName}: GetAudioRendererPerformanceMetricsSampleRate → 0 (not available)");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetAudioDeviceService — 获取 IAudioDevice</summary>
    private ResultCode GetAudioDeviceService(IpcRequest request, ref IpcResponse response)
    {
        int deviceHandle = unchecked((int)0xFFFF1100);
        response.Data.AddRange(BitConverter.GetBytes(deviceHandle));
        Logger.Debug(PortName, $"{PortName}: GetAudioDeviceService → IAudioDevice handle");
        return ResultCode.Success;
    }

    /// <summary>命令 7: OpenAudioRenderer2 — [2.0.0+] 打开 IAudioRenderer (v2)</summary>
    private ResultCode OpenAudioRenderer2(IpcRequest request, ref IpcResponse response)
    {
        int rendererHandle = unchecked((int)0xFFFF1010);
        response.Data.AddRange(BitConverter.GetBytes(rendererHandle));
        Logger.Info(PortName, $"{PortName}: OpenAudioRenderer2 → IAudioRenderer handle");
        return ResultCode.Success;
    }

    /// <summary>命令 8: GetAudioDeviceService2 — [2.0.0+] 获取 IAudioDevice (v2)</summary>
    private ResultCode GetAudioDeviceService2(IpcRequest request, ref IpcResponse response)
    {
        int deviceHandle = unchecked((int)0xFFFF1110);
        response.Data.AddRange(BitConverter.GetBytes(deviceHandle));
        Logger.Debug(PortName, $"{PortName}: GetAudioDeviceService2 → IAudioDevice handle");
        return ResultCode.Success;
    }

    /// <summary>命令 9: GetAudioDeviceServiceWithRevisionInfo — [4.0.0+] 带版本号获取 IAudioDevice</summary>
    private ResultCode GetAudioDeviceServiceWithRevisionInfo(IpcRequest request, ref IpcResponse response)
    {
        int deviceHandle = unchecked((int)0xFFFF1120);
        response.Data.AddRange(BitConverter.GetBytes(deviceHandle));
        Logger.Debug(PortName, $"{PortName}: GetAudioDeviceServiceWithRevisionInfo → IAudioDevice handle");
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetAudioDeviceServiceWithRevisionInfo2 — [4.0.0+] 带版本号获取 IAudioDevice (v2)</summary>
    private ResultCode GetAudioDeviceServiceWithRevisionInfo2(IpcRequest request, ref IpcResponse response)
    {
        int deviceHandle = unchecked((int)0xFFFF1130);
        response.Data.AddRange(BitConverter.GetBytes(deviceHandle));
        Logger.Debug(PortName, $"{PortName}: GetAudioDeviceServiceWithRevisionInfo2 → IAudioDevice handle");
        return ResultCode.Success;
    }

    internal AudRenState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>audren:u — 音频渲染器管理服务 (用户端口)</summary>
public sealed class AudRenUService : AudRenManagerBase
{
    public override string PortName => "audren:u";
    public AudRenUService(AudRenState state) : base(state) { }
}

/// <summary>audren:u2 — 音频渲染器管理服务 (用户端口 v2)</summary>
public sealed class AudRenU2Service : AudRenManagerBase
{
    public override string PortName => "audren:u2";
    public AudRenU2Service(AudRenState state) : base(state) { }
}

/// <summary>
/// IAudioRenderer — 音频渲染器接口
/// nn::audio::detail::IAudioRenderer
/// 通过 audren:u 的 OpenAudioRenderer 获取
/// 管理音频渲染循环 (RequestUpdate / Start / Stop)
/// 命令表基于 SwitchBrew Audio_services 页面和 Ryujinx 实现
/// </summary>
public sealed class AudioRendererService : IIpcService
{
    public string PortName => "audren:ren"; // 内部虚拟端口名 — 通过 OpenAudioRenderer 获取

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly AudRenState _state;

    public AudioRendererService(AudRenState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = RequestUpdateAudioRenderer,          // 请求更新音频渲染器
            [1] = StartAudioRenderer,                   // 启动音频渲染器
            [2] = StopAudioRenderer,                    // 停止音频渲染器
            [3] = QuerySystemEvent,                     // 查询系统事件
            [4] = SetRenderingTimeLimit,                 // 设置渲染时间限制
            [5] = GetRenderingTimeLimit,                 // 获取渲染时间限制
            [6] = RequestUpdateAudioRenderer2,          // [2.0.0+] 请求更新音频渲染器 (v2)
            [7] = ExecuteAudioRendererRendering,        // [3.0.0+] 执行音频渲染器渲染
        };
    }

    /// <summary>命令 0: RequestUpdateAudioRenderer — 请求更新音频渲染器</summary>
    /// <remarks>
    /// 核心渲染命令 — homebrew 每帧调用此命令提交混音参数
    /// 输入: 更新参数缓冲区; 输出: 渲染结果缓冲区
    /// 在模拟器 stub 中返回空更新响应
    /// </remarks>
    private ResultCode RequestUpdateAudioRenderer(IpcRequest request, ref IpcResponse response)
    {
        // 返回更新结果: 空的渲染响应 (0x20 bytes header)
        // 真实实现会解析参数并执行 DSP 混音
        response.Data.AddRange(new byte[0x20]);
        Logger.Debug(PortName, $"{PortName}: RequestUpdateAudioRenderer (stub, returned empty response)");
        return ResultCode.Success;
    }

    /// <summary>命令 1: StartAudioRenderer — 启动音频渲染器</summary>
    private ResultCode StartAudioRenderer(IpcRequest request, ref IpcResponse response)
    {
        _state.IsStarted = true;
        Logger.Info(PortName, $"{PortName}: StartAudioRenderer");
        return ResultCode.Success;
    }

    /// <summary>命令 2: StopAudioRenderer — 停止音频渲染器</summary>
    private ResultCode StopAudioRenderer(IpcRequest request, ref IpcResponse response)
    {
        _state.IsStarted = false;
        Logger.Info(PortName, $"{PortName}: StopAudioRenderer");
        return ResultCode.Success;
    }

    /// <summary>命令 3: QuerySystemEvent — 查询系统事件 (渲染完成通知)</summary>
    private ResultCode QuerySystemEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF1020);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, $"{PortName}: QuerySystemEvent → KEvent handle");
        return ResultCode.Success;
    }

    /// <summary>命令 4: SetRenderingTimeLimit — 设置渲染时间限制</summary>
    private ResultCode SetRenderingTimeLimit(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.AudRenResult(2);
        uint limit = BitConverter.ToUInt32(request.Data, 0);
        _state.RenderingTimeLimit = limit;
        Logger.Debug(PortName, $"{PortName}: SetRenderingTimeLimit → {limit}ms");
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetRenderingTimeLimit — 获取渲染时间限制</summary>
    private ResultCode GetRenderingTimeLimit(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.RenderingTimeLimit));
        Logger.Debug(PortName, $"{PortName}: GetRenderingTimeLimit → {_state.RenderingTimeLimit}ms");
        return ResultCode.Success;
    }

    /// <summary>命令 6: RequestUpdateAudioRenderer2 — [2.0.0+] 请求更新音频渲染器 (v2)</summary>
    private ResultCode RequestUpdateAudioRenderer2(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[0x20]);
        Logger.Debug(PortName, $"{PortName}: RequestUpdateAudioRenderer2 (stub, returned empty response)");
        return ResultCode.Success;
    }

    /// <summary>命令 7: ExecuteAudioRendererRendering — [3.0.0+] 执行音频渲染器渲染</summary>
    private ResultCode ExecuteAudioRendererRendering(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ExecuteAudioRendererRendering (stub)");
        return ResultCode.Success;
    }

    internal AudRenState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// IAudioDevice — 音频设备接口
/// nn::audio::detail::IAudioDevice
/// 通过 audren:u 的 GetAudioDeviceService 获取
/// 管理音频输出设备选择和状态查询
/// 命令表基于 SwitchBrew Audio_services 页面和 Ryujinx 实现
/// </summary>
public sealed class AudioDeviceService : IIpcService
{
    public string PortName => "auddev"; // 内部虚拟端口名 — 通过 GetAudioDeviceService 获取

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    public AudioDeviceService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = ListAudioOutputDeviceName,          // 列出音频输出设备名称
            [1]  = SetAudioOutputDeviceMode,           // 设置音频输出设备模式
            [2]  = GetAudioOutputDeviceMode,           // 获取音频输出设备模式
            [3]  = GetDeviceChannelMapping,            // 获取设备声道映射
            [4]  = GetAudioOutputDeviceName,           // 获取音频输出设备名称
            [5]  = QueryAudioOutputSystemEvent,        // [4.0.0+] 查询音频输出系统事件
            [6]  = GetAudioOutputDeviceState,          // [5.0.0+] 获取音频输出设备状态
            [7]  = RequestInputFocus,                  // [5.0.0+] 请求输入焦点
            [8]  = IsInputFocusSupported,              // [5.0.0+] 是否支持输入焦点
            [9]  = RequestAudioOutputDeviceReconfigure, // [6.0.0+] 请求音频输出设备重配置
            [10] = GetAudioOutputDeviceReconfigureSupported, // [6.0.0+] 是否支持设备重配置
        };
    }

    /// <summary>命令 0: ListAudioOutputDeviceName — 列出音频输出设备名称</summary>
    private ResultCode ListAudioOutputDeviceName(IpcRequest request, ref IpcResponse response)
    {
        // 返回设备数量 + 设备名称列表
        response.Data.AddRange(BitConverter.GetBytes(1)); // count = 1
        // 设备名 "AudioStereoR" (0x80 bytes, UTF-16LE, null-terminated)
        var nameBytes = new byte[0x80];
        var name = System.Text.Encoding.Unicode.GetBytes("AudioStereoR\0");
        name.CopyTo(nameBytes, 0);
        response.Data.AddRange(nameBytes);
        Logger.Debug(PortName, $"{PortName}: ListAudioOutputDeviceName → 1 device (AudioStereoR)");
        return ResultCode.Success;
    }

    /// <summary>命令 1: SetAudioOutputDeviceMode — 设置音频输出设备模式 (stub)</summary>
    private ResultCode SetAudioOutputDeviceMode(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: SetAudioOutputDeviceMode (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetAudioOutputDeviceMode — 获取音频输出设备模式</summary>
    private ResultCode GetAudioOutputDeviceMode(IpcRequest request, ref IpcResponse response)
    {
        // 返回设备模式: 0 = LineOut, 1 = Speaker
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(PortName, $"{PortName}: GetAudioOutputDeviceMode → LineOut");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetDeviceChannelMapping — 获取设备声道映射</summary>
    private ResultCode GetDeviceChannelMapping(IpcRequest request, ref IpcResponse response)
    {
        // 返回声道映射: 0 = Mono, 1 = Stereo, 2 = 2.1, 3 = Quad, 4 = 5.1, 5 = 7.1
        response.Data.AddRange(BitConverter.GetBytes(1U)); // Stereo
        Logger.Debug(PortName, $"{PortName}: GetDeviceChannelMapping → Stereo");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetAudioOutputDeviceName — 获取音频输出设备名称</summary>
    private ResultCode GetAudioOutputDeviceName(IpcRequest request, ref IpcResponse response)
    {
        var nameBytes = new byte[0x80];
        var name = System.Text.Encoding.Unicode.GetBytes("AudioStereoR\0");
        name.CopyTo(nameBytes, 0);
        response.Data.AddRange(nameBytes);
        Logger.Debug(PortName, $"{PortName}: GetAudioOutputDeviceName → AudioStereoR");
        return ResultCode.Success;
    }

    /// <summary>命令 5: QueryAudioOutputSystemEvent — [4.0.0+] 查询音频输出系统事件</summary>
    private ResultCode QueryAudioOutputSystemEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF1140);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, $"{PortName}: QueryAudioOutputSystemEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetAudioOutputDeviceState — [5.0.0+] 获取音频输出设备状态</summary>
    private ResultCode GetAudioOutputDeviceState(IpcRequest request, ref IpcResponse response)
    {
        // 返回设备状态: 0 = Attached
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(PortName, $"{PortName}: GetAudioOutputDeviceState → Attached");
        return ResultCode.Success;
    }

    /// <summary>命令 7: RequestInputFocus — [5.0.0+] 请求输入焦点 (stub)</summary>
    private ResultCode RequestInputFocus(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: RequestInputFocus (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 8: IsInputFocusSupported — [5.0.0+] 是否支持输入焦点</summary>
    private ResultCode IsInputFocusSupported(IpcRequest request, ref IpcResponse response)
    {
        response.Data.Add(0); // false
        Logger.Debug(PortName, $"{PortName}: IsInputFocusSupported → false");
        return ResultCode.Success;
    }

    /// <summary>命令 9: RequestAudioOutputDeviceReconfigure — [6.0.0+] 请求设备重配置 (stub)</summary>
    private ResultCode RequestAudioOutputDeviceReconfigure(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: RequestAudioOutputDeviceReconfigure (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetAudioOutputDeviceReconfigureSupported — [6.0.0+] 是否支持重配置</summary>
    private ResultCode GetAudioOutputDeviceReconfigureSupported(IpcRequest request, ref IpcResponse response)
    {
        response.Data.Add(0); // false
        Logger.Debug(PortName, $"{PortName}: GetAudioOutputDeviceReconfigureSupported → false");
        return ResultCode.Success;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
