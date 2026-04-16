using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// vi: 显示管理服务 (Video Interface)
/// 核心必选 - 管理显示输出、图层管理
/// </summary>
public sealed class ViService : IIpcService
{
    public string PortName => "vi:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>显示画面更新事件（UI 层监听此事件以获取新的帧）</summary>
    public event Action<int, int, ReadOnlySpan<byte>>? FramePresented;

    public ViService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetDisplayService,     // 获取显示服务
            [1] = GetDisplayService2,    // 获取显示服务 (版本 2)
        };
    }

    private ResultCode GetDisplayService(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(ViService), "vi: GetDisplayService");
        return ResultCode.Success;
    }

    private ResultCode GetDisplayService2(IpcRequest request, ref IpcResponse response)
    {
        Logger.Info(nameof(ViService), "vi: GetDisplayService2");
        return ResultCode.Success;
    }

    /// <summary>由图形后端调用：提交新帧到显示服务</summary>
    public void PresentFrame(int width, int height, ReadOnlySpan<byte> frameData)
    {
        FramePresented?.Invoke(width, height, frameData);
    }

    public void Dispose() { }
}
