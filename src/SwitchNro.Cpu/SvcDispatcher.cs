using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;

namespace SwitchNro.Cpu;

/// <summary>
/// SVC 系统调用分发器
/// 将 vCPU 拦截的 SVC 调用分发到对应的 C# 处理函数
/// </summary>
public sealed class SvcDispatcher
{
    private readonly Dictionary<uint, Func<SvcInfo, SvcResult>> _handlers = new();
    private readonly Dictionary<uint, string> _svcNames = new();

    public SvcDispatcher()
    {
        RegisterKnownSvcNames();
    }

    /// <summary>注册 SVC 处理函数</summary>
    public void Register(uint svcNumber, Func<SvcInfo, SvcResult> handler)
    {
        _handlers[svcNumber] = handler;
    }

    /// <summary>分发 SVC 调用</summary>
    public SvcResult Dispatch(SvcInfo svc)
    {
        var svcName = GetSvcName(svc.SvcNumber);
        Logger.Debug(nameof(SvcDispatcher), $"分发 SVC 0x{svc.SvcNumber:X2} ({svcName})");

        if (_handlers.TryGetValue(svc.SvcNumber, out var handler))
        {
            try
            {
                return handler(svc);
            }
            catch (Exception ex)
            {
                Logger.Error(nameof(SvcDispatcher), $"SVC 0x{svc.SvcNumber:X2} 处理异常: {ex.Message}");
                return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.NotImplemented) };
            }
        }

        // 未实现的 SVC — 返回未实现错误
        Logger.Warning(nameof(SvcDispatcher), $"未实现的 SVC: 0x{svc.SvcNumber:X2} ({svcName})");
        return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.NotImplemented) };
    }

    /// <summary>获取 SVC 名称</summary>
    public string GetSvcName(uint svcNumber) =>
        _svcNames.TryGetValue(svcNumber, out var name) ? name : $"Unknown_0x{svcNumber:X2}";

    private void RegisterKnownSvcNames()
    {
        // Horizon OS 主要 SVC 编号表
        _svcNames[0x01] = "SetHeapSize";
        _svcNames[0x03] = "MapMemory";
        _svcNames[0x04] = "UnmapMemory";
        _svcNames[0x05] = "QueryMemory";
        _svcNames[0x06] = "ExitProcess";
        _svcNames[0x07] = "ExitThread";
        _svcNames[0x08] = "SleepThread";
        _svcNames[0x09] = "GetThreadPriority";
        _svcNames[0x0A] = "SetThreadPriority";
        _svcNames[0x0C] = "GetCurrentProcessorNumber";
        _svcNames[0x0D] = "WaitSynchronization";
        _svcNames[0x0E] = "CancelSynchronization";
        _svcNames[0x0F] = "ArbitrateLock";
        _svcNames[0x10] = "ArbitrateUnlock";
        _svcNames[0x11] = "WaitProcessWideKeyAtomic";
        _svcNames[0x12] = "SignalProcessWideKey";
        _svcNames[0x13] = "GetSystemTick";
        _svcNames[0x14] = "ConnectToNamedPort";
        _svcNames[0x15] = "ExceptionOccurred";
        _svcNames[0x1F] = "ConnectToNamedPort";
        _svcNames[0x21] = "SendSyncRequest";
        _svcNames[0x22] = "SendSyncRequestWithUserBuffer";
        _svcNames[0x24] = "SendAsyncRequestWithUserBuffer";
        _svcNames[0x26] = "OutputDebugString";
        _svcNames[0x27] = "ReturnFromException";
        _svcNames[0x28] = "GetInfo";
        _svcNames[0x29] = "GetInfo";
        _svcNames[0x2B] = "FlushEntireDataCache";
        _svcNames[0x2C] = "FlushDataCache";
        _svcNames[0x35] = "MapPhysicalMemory";
        _svcNames[0x36] = "UnmapPhysicalMemory";
        _svcNames[0x3C] = "GetDebugFutureThreadInfo";
        _svcNames[0x3D] = "GetLastThreadInfo";
        _svcNames[0x3E] = "GetResourceLimitLimitValue";
        _svcNames[0x3F] = "GetResourceLimitCurrentValue";
        _svcNames[0x40] = "SetThreadActivity";
        _svcNames[0x41] = "GetThreadContext3";
        _svcNames[0x44] = "WaitForAddress";
        _svcNames[0x45] = "SignalToAddress";
        _svcNames[0x50] = "MapPhysicalMemoryUnsafe";
        _svcNames[0x51] = "UnmapPhysicalMemoryUnsafe";
        _svcNames[0x52] = "SetUnsafeLimit";
        _svcNames[0x60] = "ReadWriteRegister";
        _svcNames[0x61] = "CreateSharedMemory";
        _svcNames[0x62] = "MapTransferMemory";
        _svcNames[0x63] = "UnmapTransferMemory";
        _svcNames[0x65] = "CreateInterruptEvent";
        _svcNames[0x66] = "QueryPhysicalAddress";
        _svcNames[0x68] = "QueryIoMapping";
        _svcNames[0x6E] = "CreateDeviceAddressSpace";
        _svcNames[0x6F] = "AttachDeviceAddressSpace";
        _svcNames[0x70] = "MapDeviceAddressSpaceByForce";
        _svcNames[0x73] = "MapDeviceAddressSpaceAligned";
        _svcNames[0x76] = "ReadRegister";
        _svcNames[0x77] = "WriteRegister";
        _svcNames[0x78] = "ReadIoRegister";
        _svcNames[0x79] = "WriteIoRegister";
        _svcNames[0x7B] = "GetDebugThreadParam";
        _svcNames[0x7C] = "ContinueDebugEvent";
    }
}

/// <summary>SVC 处理结果</summary>
public readonly struct SvcResult
{
    /// <summary>返回码（X0）</summary>
    public ResultCode ReturnCode { get; init; }

    /// <summary>附加返回值（X1）</summary>
    public ulong ReturnValue1 { get; init; }
}
