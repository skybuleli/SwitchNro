using System;
using SwitchNro.Audio;
using SwitchNro.Common.Logging;

namespace SwitchNro.Audio.SDL2;

/// <summary>
/// SDL2 音频后端
/// 简单可靠，NRO 自制软件首选音频后端
/// </summary>
public sealed class Sdl2AudioBackend : IAudioBackend
{
    public string BackendName => "SDL2 Audio";

    public void Initialize(AudioBackendConfig config)
    {
        Logger.Info(nameof(Sdl2AudioBackend), $"SDL2 音频初始化: {config.SampleRate}Hz, {config.Channels}ch, 缓冲={config.BufferSizeMs}ms");
        // TODO: SDL_OpenAudioDevice
    }

    public void SubmitSamples(ReadOnlySpan<short> samples)
    {
        // TODO: 填充 SDL 音频缓冲区
    }

    public float GetCurrentLatencyMs() => 30f;

    public void SetVolume(float volume)
    {
        // TODO: SDL 音量控制
    }

    public void SetPause(bool paused)
    {
        // TODO: SDL_PauseAudioDevice
    }

    public void Dispose()
    {
        Logger.Info(nameof(Sdl2AudioBackend), "SDL2 音频后端已释放");
    }
}
