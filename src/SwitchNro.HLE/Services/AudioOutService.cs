using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// audout:u — 音频输出服务 (Audio Output User)
/// 核心服务 - 管理音频输出设备、音频缓冲区提交
/// Homebrew 通过此服务提交 PCM 音频采样
/// </summary>
public sealed class AudioOutService : IIpcService
{
    public string PortName => "audout:u";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>当前音频输出状态</summary>
    private bool _isAudioOutOpen;

    /// <summary>已注册的音频缓冲区数量</summary>
    private int _registeredBufferCount;

    public AudioOutService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = ListAudioOuts,               // 列出音频输出设备
            [1]  = OpenAudioOut,                // 打开音频输出
            [2]  = ListAudioOutsAuto,
            [3]  = OpenAudioOutAuto,
            [4]  = ListAudioOutsWake,
            [5]  = OpenAudioOutWake,
            [10] = GetAudioOutState,            // 获取输出状态
            [11] = StartAudioOut,               // 开始音频输出
            [12] = StopAudioOut,                // 停止音频输出
            [13] = AppendAudioOutBuffer,        // 提交音频缓冲区
            [14] = RegisterBufferEvent,         // 注册缓冲区事件
            [15] = GetReleasedAudioOutBuffer,   // 获取已释放的缓冲区
            [16] = ContainsAudioOutBuffer,
            [17] = AppendAudioOutBufferAuto,
            [18] = GetReleasedAudioOutBuffersAuto,
        };
    }

    /// <summary>命令 0: ListAudioOuts — 列出可用的音频输出设备</summary>
    private ResultCode ListAudioOuts(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(AudioOutService), "audout:u: ListAudioOuts");
        // 返回设备数量 (1 = 默认设备)
        response.Data.AddRange(BitConverter.GetBytes(1));
        // 设备名称 "MainOut" (null-terminated, 128 bytes)
        var deviceName = "MainOut\0"u8.ToArray();
        response.Data.AddRange(deviceName);
        return ResultCode.Success;
    }

    /// <summary>命令 1: OpenAudioOut — 打开音频输出设备</summary>
    private ResultCode OpenAudioOut(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(AudioOutService), "audout:u: OpenAudioOut");

        _isAudioOutOpen = true;

        // 返回音频输出参数: sampleRate, channelCount, sampleFormat
        response.Data.AddRange(BitConverter.GetBytes(48000));   // SampleRate
        response.Data.AddRange(BitConverter.GetBytes((ushort)2)); // ChannelCount
        response.Data.AddRange(BitConverter.GetBytes((ushort)1)); // SampleFormat (PCM_INT16)
        // 返回音频输出句柄
        response.CopyHandles.Add(0x300);
        return ResultCode.Success;
    }

    /// <summary>命令 2: ListAudioOutsAuto</summary>
    private ResultCode ListAudioOutsAuto(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(1));
        var deviceName = "MainOut\0"u8.ToArray();
        response.Data.AddRange(deviceName);
        return ResultCode.Success;
    }

    /// <summary>命令 3: OpenAudioOutAuto</summary>
    private ResultCode OpenAudioOutAuto(IpcRequest request, ref IpcResponse response)
    {
        _isAudioOutOpen = true;
        response.Data.AddRange(BitConverter.GetBytes(48000));
        response.Data.AddRange(BitConverter.GetBytes((ushort)2));
        response.Data.AddRange(BitConverter.GetBytes((ushort)1));
        response.CopyHandles.Add(0x300);
        return ResultCode.Success;
    }

    /// <summary>命令 4: ListAudioOutsWake</summary>
    private ResultCode ListAudioOutsWake(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(1));
        var deviceName = "MainOut\0"u8.ToArray();
        response.Data.AddRange(deviceName);
        return ResultCode.Success;
    }

    /// <summary>命令 5: OpenAudioOutWake</summary>
    private ResultCode OpenAudioOutWake(IpcRequest request, ref IpcResponse response)
    {
        _isAudioOutOpen = true;
        response.Data.AddRange(BitConverter.GetBytes(48000));
        response.Data.AddRange(BitConverter.GetBytes((ushort)2));
        response.Data.AddRange(BitConverter.GetBytes((ushort)1));
        response.CopyHandles.Add(0x300);
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetAudioOutState — 获取音频输出状态</summary>
    private ResultCode GetAudioOutState(IpcRequest request, ref IpcResponse response)
    {
        var state = _isAudioOutOpen ? AudioOutState.Started : AudioOutState.Stopped;
        response.Data.Add((byte)state);
        Logger.Debug(nameof(AudioOutService), $"audout:u: GetAudioOutState → {state}");
        return ResultCode.Success;
    }

    /// <summary>命令 11: StartAudioOut — 开始音频输出</summary>
    private ResultCode StartAudioOut(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(AudioOutService), "audout:u: StartAudioOut");
        _isAudioOutOpen = true;
        return ResultCode.Success;
    }

    /// <summary>命令 12: StopAudioOut — 停止音频输出</summary>
    private ResultCode StopAudioOut(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(AudioOutService), "audout:u: StopAudioOut");
        _isAudioOutOpen = false;
        return ResultCode.Success;
    }

    /// <summary>命令 13: AppendAudioOutBuffer — 提交音频缓冲区</summary>
    private ResultCode AppendAudioOutBuffer(IpcRequest request, ref IpcResponse response)
    {
        _registeredBufferCount++;
        Logger.Debug(nameof(AudioOutService), $"audout:u: AppendAudioOutBuffer (total={_registeredBufferCount})");
        return ResultCode.Success;
    }

    /// <summary>命令 14: RegisterBufferEvent — 注册缓冲区完成事件</summary>
    private ResultCode RegisterBufferEvent(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(AudioOutService), "audout:u: RegisterBufferEvent");
        response.CopyHandles.Add(0x400); // 虚拟事件句柄
        return ResultCode.Success;
    }

    /// <summary>命令 15: GetReleasedAudioOutBuffer — 获取已播放完毕的缓冲区</summary>
    private ResultCode GetReleasedAudioOutBuffer(IpcRequest request, ref IpcResponse response)
    {
        // 返回已释放的缓冲区数量
        var released = Math.Min(_registeredBufferCount, 1);
        _registeredBufferCount -= released;
        response.Data.AddRange(BitConverter.GetBytes(released));
        return ResultCode.Success;
    }

    /// <summary>命令 16: ContainsAudioOutBuffer</summary>
    private ResultCode ContainsAudioOutBuffer(IpcRequest request, ref IpcResponse response)
    {
        response.Data.Add(0); // false
        return ResultCode.Success;
    }

    /// <summary>命令 17: AppendAudioOutBufferAuto</summary>
    private ResultCode AppendAudioOutBufferAuto(IpcRequest request, ref IpcResponse response)
    {
        _registeredBufferCount++;
        return ResultCode.Success;
    }

    /// <summary>命令 18: GetReleasedAudioOutBuffersAuto</summary>
    private ResultCode GetReleasedAudioOutBuffersAuto(IpcRequest request, ref IpcResponse response)
    {
        var released = Math.Min(_registeredBufferCount, 1);
        _registeredBufferCount -= released;
        response.Data.AddRange(BitConverter.GetBytes(released));
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>音频输出状态</summary>
internal enum AudioOutState : byte
{
    Started = 0,
    Stopped = 1,
}
