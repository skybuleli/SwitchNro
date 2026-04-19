using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;
using SwitchNro.Memory;

namespace SwitchNro.HLE.Services;

/// <summary>
/// vi: 显示管理服务 (Video Interface)
/// 核心必选 - 管理显示输出、图层管理
/// </summary>
public sealed class ViService : IIpcService
{
    private readonly string _portName;
    public string PortName => _portName;

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>显示画面更新事件（UI 层监听此事件以获取新的帧）</summary>
    public event Action<int, int, ReadOnlySpan<byte>>? FramePresented;

    public ViService(string portName = "vi:m")
    {
        _portName = portName;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [100] = GetDisplayService,   // GetDisplayService
            [101] = GetDisplayService,   // GetDisplayService (Version 2)
            
            // 以下是 IDisplayService 的命令 (通常通过 OpenDisplayService 获得)
            // 简化：直接在主服务中响应，因为目前 IPC 框架暂不支持多层 Session 代理
            [1010] = OpenDisplay,        // OpenDisplay
            [2010] = CreateLayer,        // CreateLayer
            [2020] = OpenLayer,          // OpenLayer
        };
    }

    private ResultCode GetDisplayService(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(ViService), $"{_portName}: GetDisplayService");
        return ResultCode.Success;
    }

    private ResultCode OpenDisplay(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(ViService), $"{_portName}: OpenDisplay");
        // 返回显示器名称 "Default" (8 字节)
        byte[] name = System.Text.Encoding.ASCII.GetBytes("Default\0");
        response.Data.AddRange(name);
        return ResultCode.Success;
    }

    private ResultCode CreateLayer(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(ViService), $"{_portName}: CreateLayer");
        // 返回 LayerId (u64)
        response.Data.AddRange(BitConverter.GetBytes(1UL));
        return ResultCode.Success;
    }

    private ResultCode OpenLayer(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(ViService), $"{_portName}: OpenLayer");
        // 返回 NativeWindow 句柄大小 (u64)
        response.Data.AddRange(BitConverter.GetBytes(0x100UL)); 
        return ResultCode.Success;
    }

    /// <summary>
    /// 模拟渲染逻辑：直接接收 Guest 内存中的 Framebuffer
    /// </summary>
    public void RequestFrameUpdate(VirtualMemoryManager memory, ulong vaddr, int width, int height)
    {
        try
        {
            int size = width * height * 4; // RGBA8888
            byte[] frame = new byte[size];
            memory.Read(vaddr, frame);
            FramePresented?.Invoke(width, height, frame);
        }
        catch (Exception ex)
        {
            Logger.Error(nameof(ViService), $"渲染帧读取失败: {ex.Message}");
        }
    }

    /// <summary>由图形后端调用：提交新帧到显示服务</summary>
    public void PresentFrame(int width, int height, ReadOnlySpan<byte> frameData)
    {
        FramePresented?.Invoke(width, height, frameData);
    }

    public void Dispose() { }
}
