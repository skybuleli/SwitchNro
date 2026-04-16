using System;
using SwitchNro.Audio;
using SwitchNro.Common.Logging;

namespace SwitchNro.Audio.CoreAudio;

/// <summary>
/// CoreAudio 低延迟音频后端
/// macOS 原生，延迟可降至 &lt;10ms
/// Phase 3 实现
/// </summary>
public sealed class CoreAudioBackend : IAudioBackend
{
    public string BackendName => "CoreAudio";

    public void Initialize(AudioBackendConfig config)
    {
        Logger.Info(nameof(CoreAudioBackend), $"CoreAudio 初始化: {config.SampleRate}Hz, 延迟目标&lt;10ms");
        // TODO: AUAudioUnit / AVAudioEngine 实现
    }

    public void SubmitSamples(ReadOnlySpan<short> samples) => throw new NotImplementedException();
    public float GetCurrentLatencyMs() => 8f;
    public void SetVolume(float volume) { }
    public void SetPause(bool paused) { }
    public void Dispose() { }
}
