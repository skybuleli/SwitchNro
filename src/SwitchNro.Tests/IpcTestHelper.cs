using SwitchNro.Common;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.Tests;

/// <summary>
/// IPC 服务测试共享辅助工具
/// </summary>
internal static class IpcTestHelper
{
    /// <summary>创建空的 IpcRequest</summary>
    public static IpcRequest EmptyRequest(uint commandId) => new()
    {
        Header = new IpcMessageHeader(0, 0),
        CommandId = commandId,
        Data = [],
    };

    /// <summary>创建带数据的 IpcRequest</summary>
    public static IpcRequest RequestWithData(uint commandId, byte[] data) => new()
    {
        Header = new IpcMessageHeader(0, 0),
        CommandId = commandId,
        Data = data,
    };

    /// <summary>调用服务命令并返回响应</summary>
    public static (ResultCode Result, IpcResponse Response) InvokeCommand(
        IIpcService service, uint commandId, byte[]? data = null)
    {
        var request = data != null ? RequestWithData(commandId, data) : EmptyRequest(commandId);
        var response = new IpcResponse();

        if (!service.CommandTable.TryGetValue(commandId, out var handler))
            return (ResultCode.SfResult(2), response);

        var result = handler(request, ref response);
        return (result, response);
    }
}
