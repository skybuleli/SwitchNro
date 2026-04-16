using System;

namespace SwitchNro.Audio;

/// <summary>
/// 音频后端接口
/// SDL2 和 CoreAudio 后端均实现此接口
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>初始化音频后端</summary>
    void Initialize(AudioBackendConfig config);

    /// <summary>提交音频采样数据</summary>
    void SubmitSamples(ReadOnlySpan<short> samples);

    /// <summary>获取当前播放延迟（毫秒）</summary>
    float GetCurrentLatencyMs();

    /// <summary>设置音量 (0.0 - 1.0)</summary>
    void SetVolume(float volume);

    /// <summary>暂停/恢复音频</summary>
    void SetPause(bool paused);

    /// <summary>后端名称</summary>
    string BackendName { get; }
}

/// <summary>音频后端配置</summary>
public sealed class AudioBackendConfig
{
    public int SampleRate { get; init; } = 48000; // Switch 默认 48kHz
    public int Channels { get; init; } = 2;       // 立体声
    public int BufferSizeMs { get; init; } = 50;   // 缓冲区大小（毫秒）
    public float Volume { get; init; } = 1.0f;
}
