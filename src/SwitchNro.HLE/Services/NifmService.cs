using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// nifm:u — 网络接口管理服务 (用户端口)
/// nn::nifm::detail::IStaticService
/// 提供网络状态查询接口，Guest 进程通过此服务获取 IGeneralService
/// </summary>
public sealed class NifmUService : IIpcService
{
    public string PortName => "nifm:u";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly NifmGeneralService _generalService;

    public NifmUService(NifmGeneralService generalService)
    {
        _generalService = generalService;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetGeneralService,     // 获取 IGeneralService 会话
        };
    }

    private ResultCode GetGeneralService(IpcRequest request, ref IpcResponse response)
    {
        // 返回虚拟的 IGeneralService 对象句柄
        int handle = unchecked((int)0xFFFF0500);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NifmUService), "nifm:u: GetGeneralService → IGeneralService session");
        return ResultCode.Success;
    }

    /// <summary>获取共享的 IGeneralService 实例</summary>
    internal NifmGeneralService GeneralService => _generalService;

    public void Dispose() { }
}

/// <summary>
/// nifm:s — 网络接口管理服务 (系统端口)
/// 与 nifm:u 功能相同，权限更高
/// </summary>
public sealed class NifmSService : IIpcService
{
    public string PortName => "nifm:s";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly NifmGeneralService _generalService;

    public NifmSService(NifmGeneralService generalService)
    {
        _generalService = generalService;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetGeneralService,
        };
    }

    private ResultCode GetGeneralService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0501);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NifmSService), "nifm:s: GetGeneralService → IGeneralService session");
        return ResultCode.Success;
    }

    internal NifmGeneralService GeneralService => _generalService;

    public void Dispose() { }
}

/// <summary>
/// nifm:a — 网络接口管理服务 (管理员端口)
/// 仅限系统进程使用
/// </summary>
public sealed class NifmAService : IIpcService
{
    public string PortName => "nifm:a";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly NifmGeneralService _generalService;

    public NifmAService(NifmGeneralService generalService)
    {
        _generalService = generalService;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetGeneralService,
        };
    }

    private ResultCode GetGeneralService(IpcRequest request, ref IpcResponse response)
    {
        int handle = unchecked((int)0xFFFF0502);
        response.Data.AddRange(BitConverter.GetBytes(handle));
        Logger.Debug(nameof(NifmAService), "nifm:a: GetGeneralService → IGeneralService session");
        return ResultCode.Success;
    }

    internal NifmGeneralService GeneralService => _generalService;

    public void Dispose() { }
}

/// <summary>
/// IGeneralService — 网络通用查询服务 (nn::nifm::detail::IGeneralService)
/// 提供网络状态、IP 地址、网络配置等查询功能
/// 命令表基于 SwitchBrew NIFM_services 页面
/// </summary>
public sealed class NifmGeneralService : IIpcService
{
    public string PortName => "nifm:gen"; // 内部虚拟端口名 — IGeneralService 通过 nifm:u/s/a GetGeneralService 获取，不注册为命名端口

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>模拟的当前 IP 地址（127.0.0.1 = 0x7F000001）</summary>
    private uint _ipAddress = 0x7F000001;

    /// <summary>网络连接状态: 0=None, 1=Connecting, 2=Connected</summary>
    private uint _connectionStatus = 2; // 默认已连接

    /// <summary>Wi-Fi 信号强度 (0-3)</summary>
    private uint _wifiSignalStrength = 3;

    /// <summary>网络配置文件数据（简化版）</summary>
    private byte[] _networkProfile = new byte[0x17C]; // NetworkProfileData 标准大小

    public NifmGeneralService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = GetRequestState,               // 获取请求状态
            [1] = GetAppletInfo,                 // 获取 Applet 信息
            [2] = GetCurrentNetworkProfile,      // 获取当前网络配置
            [3] = GetInternetConnectionStatus,    // 获取互联网连接状态
            [4] = GetCurrentIpAddress,            // 获取当前 IP 地址
        };
    }

    /// <summary>命令 0: GetRequestState — 获取请求状态</summary>
    private ResultCode GetRequestState(IpcRequest request, ref IpcResponse response)
    {
        // 返回 u32 requestState (0=Pending, 1=Available, 2=NotAvailable)
        response.Data.AddRange(BitConverter.GetBytes(1U)); // Available
        Logger.Debug(nameof(NifmGeneralService), "nifm:gen: GetRequestState → Available");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetAppletInfo — 获取 Applet 信息（stub）</summary>
    private ResultCode GetAppletInfo(IpcRequest request, ref IpcResponse response)
    {
        // 输入: u64 appletId
        // 返回: u32 result + AppletInfo(0x48 bytes) — stub
        response.Data.AddRange(BitConverter.GetBytes(0U)); // result = 0 (success)
        response.Data.AddRange(new byte[0x48]); // AppletInfo 空白填充
        Logger.Debug(nameof(NifmGeneralService), "nifm:gen: GetAppletInfo (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetCurrentNetworkProfile — 获取当前网络配置</summary>
    private ResultCode GetCurrentNetworkProfile(IpcRequest request, ref IpcResponse response)
    {
        // 返回 NetworkProfileData (0x17C bytes)
        response.Data.AddRange(_networkProfile);
        Logger.Debug(nameof(NifmGeneralService), $"nifm:gen: GetCurrentNetworkProfile → {_networkProfile.Length} bytes");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetInternetConnectionStatus — 获取互联网连接状态</summary>
    private ResultCode GetInternetConnectionStatus(IpcRequest request, ref IpcResponse response)
    {
        // TODO: 实际 InternetConnectionStatus 结构体可能更大（含额外填充字段）
        // 返回: u8 type(0=None/1=WiFi/2=Ethernet) + u8 wifiStrength(0-3) + u8 state(0=None/1=Connecting/2=Connected) + padding
        response.Data.Add(1);                // type = WiFi
        response.Data.Add((byte)_wifiSignalStrength); // signal strength
        response.Data.Add((byte)_connectionStatus);   // connection state
        response.Data.Add(0);                // padding
        Logger.Debug(nameof(NifmGeneralService), $"nifm:gen: GetInternetConnectionStatus → WiFi, strength={_wifiSignalStrength}, state={_connectionStatus}");
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetCurrentIpAddress — 获取当前 IP 地址</summary>
    private ResultCode GetCurrentIpAddress(IpcRequest request, ref IpcResponse response)
    {
        // 返回: u32 ipAddress (little-endian IPv4)
        response.Data.AddRange(BitConverter.GetBytes(_ipAddress));
        Logger.Debug(nameof(NifmGeneralService), $"nifm:gen: GetCurrentIpAddress → {_ipAddress:X8}");
        return ResultCode.Success;
    }

    /// <summary>外部可设置/查询 IP 地址</summary>
    public uint IpAddress
    {
        get => _ipAddress;
        set => _ipAddress = value;
    }

    /// <summary>外部可设置/查询连接状态</summary>
    public uint ConnectionStatus
    {
        get => _connectionStatus;
        set => _connectionStatus = value;
    }

    /// <summary>外部可设置/查询 Wi-Fi 信号强度</summary>
    public uint WifiSignalStrength
    {
        get => _wifiSignalStrength;
        set => _wifiSignalStrength = value;
    }

    /// <summary>外部可设置网络配置文件数据</summary>
    public byte[] NetworkProfile
    {
        get => _networkProfile;
        set => _networkProfile = value;
    }

    public void Dispose() { }
}
