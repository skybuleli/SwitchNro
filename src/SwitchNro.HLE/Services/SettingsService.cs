using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// set: — 系统设置服务 (System Settings)
/// 核心必选 - 提供系统语言、区域、固件版本等设置查询
/// Homebrew 启动时通常查询语言和区域设置
/// </summary>
public sealed class SettingsService : IIpcService
{
    public string PortName => "set:";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>系统语言代码 (en-US) — little-endian uint64: bytes 65 6E 2D 55 53 00 00 00</summary>
    private const ulong LanguageCode = 0x000000_53552D_6E65UL;

    public SettingsService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = GetLanguageCode,           // 获取语言代码
            [1]  = SetLanguageCode,           // 设置语言代码
            [2]  = GetAvailableLanguageCodes, // 获取可用语言列表
            [3]  = MakeLanguageCode,          // 构造语言代码
            [4]  = GetAvailableLanguageCodeCount,
            [5]  = GetAvailableLanguageCodes2,
            [6]  = GetAvailableLanguageCodeCount2,
            [7]  = GetKeyboardLayout,         // 获取键盘布局
            [8]  = GetRegionCode,             // 获取区域代码
            [9]  = GetRegionCode2,
            [10] = GetDeviceNickName,         // 获取设备昵称
        };
    }

    /// <summary>命令 0: GetLanguageCode — 获取当前系统语言</summary>
    private ResultCode GetLanguageCode(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SettingsService), "set: GetLanguageCode → en-US");
        response.Data.AddRange(BitConverter.GetBytes(LanguageCode));
        return ResultCode.Success;
    }

    /// <summary>命令 1: SetLanguageCode — 设置系统语言</summary>
    private ResultCode SetLanguageCode(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SettingsService), "set: SetLanguageCode");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetAvailableLanguageCodes — 获取可用语言代码列表</summary>
    private ResultCode GetAvailableLanguageCodes(IpcRequest request, ref IpcResponse response)
    {
        // 返回支持的 Switch 语言数量
        response.Data.AddRange(BitConverter.GetBytes(16)); // 16 种语言
        return ResultCode.Success;
    }

    /// <summary>命令 3: MakeLanguageCode — 从语言 ID 构造语言代码</summary>
    private ResultCode MakeLanguageCode(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length >= 4)
        {
            uint languageId = BitConverter.ToUInt32(request.Data, 0);
            // 映射到标准语言代码 (简化：统一返回 en-US)
            response.Data.AddRange(BitConverter.GetBytes(LanguageCode));
            Logger.Debug(nameof(SettingsService), $"set: MakeLanguageCode(id={languageId})");
        }
        return ResultCode.Success;
    }

    /// <summary>命令 4: GetAvailableLanguageCodeCount</summary>
    private ResultCode GetAvailableLanguageCodeCount(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(16));
        return ResultCode.Success;
    }

    /// <summary>命令 5: GetAvailableLanguageCodes2</summary>
    private ResultCode GetAvailableLanguageCodes2(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(16));
        return ResultCode.Success;
    }

    /// <summary>命令 6: GetAvailableLanguageCodeCount2</summary>
    private ResultCode GetAvailableLanguageCodeCount2(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(16));
        return ResultCode.Success;
    }

    /// <summary>命令 7: GetKeyboardLayout</summary>
    private ResultCode GetKeyboardLayout(IpcRequest request, ref IpcResponse response)
    {
        // 返回键盘布局 ID (0 = English US)
        response.Data.AddRange(BitConverter.GetBytes(0));
        return ResultCode.Success;
    }

    /// <summary>命令 8: GetRegionCode — 获取区域代码</summary>
    private ResultCode GetRegionCode(IpcRequest request, ref IpcResponse response)
    {
        // 返回区域代码 (1 = Americas)
        response.Data.AddRange(BitConverter.GetBytes(1));
        return ResultCode.Success;
    }

    /// <summary>命令 9: GetRegionCode2</summary>
    private ResultCode GetRegionCode2(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(1));
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetDeviceNickName — 获取设备昵称</summary>
    private ResultCode GetDeviceNickName(IpcRequest request, ref IpcResponse response)
    {
        var nickname = "SwitchNro\0"u8.ToArray();
        response.Data.AddRange(nickname);
        Logger.Debug(nameof(SettingsService), "set: GetDeviceNickName → SwitchNro");
        return ResultCode.Success;
    }

    public void Dispose() { }
}

/// <summary>
/// set:sys — 系统设置服务 (内部)
/// 提供固件版本、序列号等系统级信息
/// </summary>
public sealed class SetSysService : IIpcService
{
    public string PortName => "set:sys";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>固件版本 (19.0.1)</summary>
    private const int FirmwareVersionMajor = 19;
    private const int FirmwareVersionMinor = 0;
    private const int FirmwareVersionPatch = 1;

    public SetSysService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]  = GetFirmwareVersion,         // 获取固件版本
            [1]  = GetFirmwareVersion2,
            [2]  = GetSerialNumber,            // 获取序列号
            [3]  = GetDeviceCert,              // 获取设备证书
            [40] = GetSystemUpdateEvent,       // 获取系统更新事件
        };
    }

    /// <summary>命令 0: GetFirmwareVersion — 获取固件版本字符串</summary>
    private ResultCode GetFirmwareVersion(IpcRequest request, ref IpcResponse response)
    {
        // 返回固件版本结构 (0x100 字节)
        var versionStr = System.Text.Encoding.ASCII.GetBytes($"{FirmwareVersionMajor}.{FirmwareVersionMinor}.{FirmwareVersionPatch}\0");
        response.Data.AddRange(versionStr);
        Logger.Debug(nameof(SetSysService), $"set:sys: GetFirmwareVersion → {FirmwareVersionMajor}.{FirmwareVersionMinor}.{FirmwareVersionPatch}");
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetFirmwareVersion2</summary>
    private ResultCode GetFirmwareVersion2(IpcRequest request, ref IpcResponse response)
    {
        var versionStr = System.Text.Encoding.ASCII.GetBytes($"{FirmwareVersionMajor}.{FirmwareVersionMinor}.{FirmwareVersionPatch}\0");
        response.Data.AddRange(versionStr);
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetSerialNumber</summary>
    private ResultCode GetSerialNumber(IpcRequest request, ref IpcResponse response)
    {
        var serial = "XWJ00000000000\0"u8.ToArray();
        response.Data.AddRange(serial);
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetDeviceCert</summary>
    private ResultCode GetDeviceCert(IpcRequest request, ref IpcResponse response)
    {
        // 返回虚拟设备证书
        response.Data.AddRange(new byte[0x240]);
        return ResultCode.Success;
    }

    /// <summary>命令 40: GetSystemUpdateEvent</summary>
    private ResultCode GetSystemUpdateEvent(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SetSysService), "set:sys: GetSystemUpdateEvent");
        return ResultCode.Success;
    }

    public void Dispose() { }
}
