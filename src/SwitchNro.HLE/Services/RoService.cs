using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 RO 服务状态
/// </summary>
public sealed class RoState
{
    /// <summary>已加载的 NRO 模块列表</summary>
    private readonly List<NroModule> _loadedModules = new();

    /// <summary>已注册的 NRR 模块列表</summary>
    private readonly List<NrrModule> _registeredModules = new();

    /// <summary>下一个 NRO 加载地址</summary>
    private ulong _nextNroAddress = 0x0800_0000;

    /// <summary>获取已加载的 NRO 模块</summary>
    public IReadOnlyList<NroModule> LoadedModules => _loadedModules;

    /// <summary>获取已注册的 NRR 模块</summary>
    public IReadOnlyList<NrrModule> RegisteredModules => _registeredModules;

    /// <summary>注册 NRO 加载并返回映射地址</summary>
    public ulong RegisterNro(ulong nroAddress, ulong nroSize, ulong bssAddress, ulong bssSize)
    {
        var module = new NroModule
        {
            NroAddress = nroAddress,
            NroSize = nroSize,
            BssAddress = bssAddress,
            BssSize = bssSize,
            MappedAddress = _nextNroAddress,
        };
        _loadedModules.Add(module);
        _nextNroAddress += nroSize + bssSize;
        // 按 0x1000 对齐
        _nextNroAddress = (_nextNroAddress + 0xFFF) & ~0xFFFUL;
        return module.MappedAddress;
    }

    /// <summary>注销 NRO 加载</summary>
    public bool UnregisterNro(ulong nroAddress)
    {
        for (int i = 0; i < _loadedModules.Count; i++)
        {
            if (_loadedModules[i].NroAddress == nroAddress)
            {
                _loadedModules.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>注册 NRR 模块信息</summary>
    public void RegisterNrr(ulong nrrAddress, ulong nrrSize)
    {
        _registeredModules.Add(new NrrModule { NrrAddress = nrrAddress, NrrSize = nrrSize });
    }

    /// <summary>注销 NRR 模块信息</summary>
    public bool UnregisterNrr(ulong nrrAddress)
    {
        for (int i = 0; i < _registeredModules.Count; i++)
        {
            if (_registeredModules[i].NrrAddress == nrrAddress)
            {
                _registeredModules.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
}

/// <summary>NRO 模块信息</summary>
public sealed class NroModule
{
    public ulong NroAddress { get; set; }
    public ulong NroSize { get; set; }
    public ulong BssAddress { get; set; }
    public ulong BssSize { get; set; }
    public ulong MappedAddress { get; set; }
}

/// <summary>NRR 模块信息</summary>
public sealed class NrrModule
{
    public ulong NrrAddress { get; set; }
    public ulong NrrSize { get; set; }
}

/// <summary>
/// RO 服务基类 — ro:1/ro:1a 共享的命令处理逻辑
/// nn::ro::detail::IRoInterface
/// ro:1 和 ro:1a 命令表完全相同，仅端口名和权限不同
/// 命令表基于 SwitchBrew RO_services 页面
/// </summary>
public abstract class RoServiceBase : IIpcService
{
    public abstract string PortName { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly RoState _state;

    protected RoServiceBase(RoState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = MapManualLoadModuleMemory,               // 映射手动加载模块内存
            [1]  = UnmapManualLoadModuleMemory,             // 取消映射手动加载模块内存
            [2]  = RegisterModuleInfo,                      // 注册模块信息
            [3]  = UnregisterModuleInfo,                    // 注销模块信息
            [4]  = RegisterProcessHandle,                   // 注册进程句柄
            [10] = RegisterModuleInfoWithUserProcessHandle, // [7.0.0+] 带进程句柄注册模块信息
        };
    }

    /// <summary>
    /// 命令 0: MapManualLoadModuleMemory — 映射手动加载模块内存
    /// 输入: PID, nro_address, nro_size, bss_address, bss_size
    /// 输出: mapped_address
    /// </summary>
    private ResultCode MapManualLoadModuleMemory(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 40) return ResultCode.RoResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        ulong nroAddress = BitConverter.ToUInt64(request.Data, 8);
        ulong nroSize = BitConverter.ToUInt64(request.Data, 16);
        ulong bssAddress = BitConverter.ToUInt64(request.Data, 24);
        ulong bssSize = BitConverter.ToUInt64(request.Data, 32);

        ulong mappedAddress = _state.RegisterNro(nroAddress, nroSize, bssAddress, bssSize);
        response.Data.AddRange(BitConverter.GetBytes(mappedAddress));
        Logger.Info(PortName, $"{PortName}: MapManualLoadModuleMemory(nro=0x{nroAddress:X16}, size=0x{nroSize:X16}, bss=0x{bssAddress:X16}, bss_size=0x{bssSize:X16}) → mapped=0x{mappedAddress:X16}");
        return ResultCode.Success;
    }

    /// <summary>
    /// 命令 1: UnmapManualLoadModuleMemory — 取消映射手动加载模块内存
    /// 输入: PID, nro_address
    /// </summary>
    private ResultCode UnmapManualLoadModuleMemory(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16) return ResultCode.RoResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        ulong nroAddress = BitConverter.ToUInt64(request.Data, 8);

        bool found = _state.UnregisterNro(nroAddress);
        if (!found)
        {
            Logger.Warning(PortName, $"{PortName}: UnmapManualLoadModuleMemory(nro=0x{nroAddress:X16}) — NRO not found");
            return ResultCode.RoResult(6); // InvalidAddress
        }
        Logger.Info(PortName, $"{PortName}: UnmapManualLoadModuleMemory(nro=0x{nroAddress:X16})");
        return ResultCode.Success;
    }

    /// <summary>
    /// 命令 2: RegisterModuleInfo — 注册模块信息 (NRR)
    /// 输入: PID, nrr_address, nrr_size
    /// </summary>
    private ResultCode RegisterModuleInfo(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 24) return ResultCode.RoResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        ulong nrrAddress = BitConverter.ToUInt64(request.Data, 8);
        ulong nrrSize = BitConverter.ToUInt64(request.Data, 16);

        _state.RegisterNrr(nrrAddress, nrrSize);
        Logger.Debug(PortName, $"{PortName}: RegisterModuleInfo(nrr=0x{nrrAddress:X16}, size=0x{nrrSize:X16})");
        return ResultCode.Success;
    }

    /// <summary>
    /// 命令 3: UnregisterModuleInfo — 注销模块信息 (NRR)
    /// 输入: PID, nrr_address
    /// </summary>
    private ResultCode UnregisterModuleInfo(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16) return ResultCode.RoResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        ulong nrrAddress = BitConverter.ToUInt64(request.Data, 8);

        bool found = _state.UnregisterNrr(nrrAddress);
        if (!found)
        {
            Logger.Warning(PortName, $"{PortName}: UnregisterModuleInfo(nrr=0x{nrrAddress:X16}) — NRR not found");
            return ResultCode.RoResult(6);
        }
        Logger.Debug(PortName, $"{PortName}: UnregisterModuleInfo(nrr=0x{nrrAddress:X16})");
        return ResultCode.Success;
    }

    /// <summary>
    /// 命令 4: RegisterProcessHandle — 注册进程句柄 (stub)
    /// 输入: PID, process_handle
    /// </summary>
    private ResultCode RegisterProcessHandle(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8) return ResultCode.RoResult(2);
        Logger.Debug(PortName, $"{PortName}: RegisterProcessHandle (stub)");
        return ResultCode.Success;
    }

    /// <summary>
    /// 命令 10: RegisterModuleInfoWithUserProcessHandle — [7.0.0+] 带进程句柄注册模块信息
    /// 输入: PID, process_handle, nrr_address, nrr_size
    /// </summary>
    private ResultCode RegisterModuleInfoWithUserProcessHandle(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 32) return ResultCode.RoResult(2);
        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        // process_handle 在 offset 8, 跳过
        ulong nrrAddress = BitConverter.ToUInt64(request.Data, 16);
        ulong nrrSize = BitConverter.ToUInt64(request.Data, 24);

        _state.RegisterNrr(nrrAddress, nrrSize);
        Logger.Debug(PortName, $"{PortName}: RegisterModuleInfoWithUserProcessHandle(nrr=0x{nrrAddress:X16}, size=0x{nrrSize:X16})");
        return ResultCode.Success;
    }

    internal RoState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>ro:1 — 可重定位对象服务 (用户端口)</summary>
public sealed class Ro1Service : RoServiceBase
{
    public override string PortName => "ro:1";
    public Ro1Service(RoState state) : base(state) { }
}

/// <summary>ro:1a — 可重定位对象服务 (应用端口)</summary>
public sealed class Ro1aService : RoServiceBase
{
    public override string PortName => "ro:1a";
    public Ro1aService(RoState state) : base(state) { }
}
