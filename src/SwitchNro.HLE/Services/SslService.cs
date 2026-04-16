using System;
using System.Collections.Generic;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// ssl — SSL 服务 (nn::ssl::sf::ISslService)
/// 提供 SSL/TLS 上下文创建、证书管理等功能
/// 命令表基于 SwitchBrew SSL_services 页面
/// </summary>
public sealed class SslService : IIpcService
{
    public string PortName => "ssl";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>接口版本号（由 SetInterfaceVersion 设置）</summary>
    private uint _interfaceVersion = 1;

    /// <summary>当前活跃的 SSL 上下文计数</summary>
    private int _contextCount;

    /// <summary>下一个虚拟 SSL 上下文句柄</summary>
    private uint _nextContextHandle = 0xD0000000;

    /// <summary>已注册的可信证书数量</summary>
    private int _trustedCertificateCount;

    /// <summary>调试选项</summary>
    private uint _debugOption;

    public SslService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0]   = CreateContext,               // 创建 SSL 上下文
            [1]   = GetContextCount,             // 获取上下文计数
            [2]   = GetCertificates,             // 获取证书列表
            [3]   = GetCertificateBufSize,       // 获取证书缓冲区大小
            [4]   = DebugIoctl,                  // 调试 IOCTL（stub）
            [5]   = SetInterfaceVersion,         // 设置接口版本
            [6]   = FlushSessionCache,           // 刷新会话缓存
            [7]   = SetDebugOption,              // 设置调试选项
            [8]   = GetDebugOption,              // 获取调试选项
            [9]   = ClearTls12FallbackFlag,      // 清除 TLS 1.2 回退标志
            [10]  = GetCertificateByIndex,       // [21.0.0+] 按索引获取证书
            [11]  = GetTrustedCertificateCount,  // [21.0.0+] 获取可信证书计数
            [100] = CreateContextForSystem,      // 为系统创建 SSL 上下文
        };
    }

    /// <summary>命令 0: CreateContext — 创建 SSL 上下文</summary>
    private ResultCode CreateContext(IpcRequest request, ref IpcResponse response)
    {
        // 输入: u32 sslVersion + padding(4)
        if (request.Data.Length < 8)
            return ResultCode.SslResult(2); // Invalid size

        uint sslVersion = BitConverter.ToUInt32(request.Data, 0);
        int handle = unchecked((int)_nextContextHandle++);
        _contextCount++;

        Logger.Info(nameof(SslService), $"ssl: CreateContext(sslVersion={sslVersion}) → handle=0x{handle:X8}");

        // 返回 ISslContext 对象句柄
        response.Data.AddRange(BitConverter.GetBytes(handle));
        return ResultCode.Success;
    }

    /// <summary>命令 1: GetContextCount — 获取当前上下文计数</summary>
    private ResultCode GetContextCount(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SslService), $"ssl: GetContextCount → {_contextCount}");
        response.Data.AddRange(BitConverter.GetBytes(_contextCount));
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetCertificates — 获取证书列表</summary>
    private ResultCode GetCertificates(IpcRequest request, ref IpcResponse response)
    {
        // 输入: u32 maxCertificates + u64 pid
        if (request.Data.Length < 12)
            return ResultCode.SslResult(2);

        uint maxCerts = BitConverter.ToUInt32(request.Data, 0);

        // 返回: s32 count + 证书数据（stub: 返回 0 个证书）
        response.Data.AddRange(BitConverter.GetBytes(0)); // s32 count = 0
        Logger.Debug(nameof(SslService), $"ssl: GetCertificates(maxCerts={maxCerts}) → 0 certificates");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetCertificateBufSize — 获取证书缓冲区大小</summary>
    private ResultCode GetCertificateBufSize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 12)
            return ResultCode.SslResult(2);

        // 返回: u64 bufSize + u32 count
        response.Data.AddRange(BitConverter.GetBytes(0UL)); // bufSize = 0
        response.Data.AddRange(BitConverter.GetBytes(0U));  // count = 0
        Logger.Debug(nameof(SslService), "ssl: GetCertificateBufSize → size=0, count=0");
        return ResultCode.Success;
    }

    /// <summary>命令 4: DebugIoctl — 调试 IOCTL（stub）</summary>
    private ResultCode DebugIoctl(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SslService), "ssl: DebugIoctl (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 5: SetInterfaceVersion — 设置接口版本</summary>
    private ResultCode SetInterfaceVersion(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4)
            return ResultCode.SslResult(2);

        _interfaceVersion = BitConverter.ToUInt32(request.Data, 0);
        Logger.Info(nameof(SslService), $"ssl: SetInterfaceVersion(version={_interfaceVersion})");
        return ResultCode.Success;
    }

    /// <summary>命令 6: FlushSessionCache — 刷新会话缓存</summary>
    private ResultCode FlushSessionCache(IpcRequest request, ref IpcResponse response)
    {
        // 输入: u64 pid (可选)
        Logger.Debug(nameof(SslService), "ssl: FlushSessionCache");
        return ResultCode.Success;
    }

    /// <summary>命令 7: SetDebugOption — 设置调试选项</summary>
    private ResultCode SetDebugOption(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4)
            return ResultCode.SslResult(2);

        _debugOption = BitConverter.ToUInt32(request.Data, 0);
        Logger.Debug(nameof(SslService), $"ssl: SetDebugOption(option=0x{_debugOption:X8})");
        return ResultCode.Success;
    }

    /// <summary>命令 8: GetDebugOption — 获取调试选项</summary>
    private ResultCode GetDebugOption(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_debugOption));
        Logger.Debug(nameof(SslService), $"ssl: GetDebugOption → 0x{_debugOption:X8}");
        return ResultCode.Success;
    }

    /// <summary>命令 9: ClearTls12FallbackFlag — 清除 TLS 1.2 回退标志</summary>
    private ResultCode ClearTls12FallbackFlag(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(SslService), "ssl: ClearTls12FallbackFlag");
        return ResultCode.Success;
    }

    /// <summary>命令 10: GetCertificateByIndex — [21.0.0+] 按索引获取证书</summary>
    private ResultCode GetCertificateByIndex(IpcRequest request, ref IpcResponse response)
    {
        if (_interfaceVersion < 21)
            return ResultCode.SslResult(6); // Version mismatch

        // 输入: u32 index + padding(4)
        if (request.Data.Length < 8)
            return ResultCode.SslResult(2);

        uint index = BitConverter.ToUInt32(request.Data, 0);
        // stub: 无证书数据
        response.Data.AddRange(BitConverter.GetBytes(0)); // s32 status = 0 (not found)
        Logger.Debug(nameof(SslService), $"ssl: GetCertificateByIndex(index={index}) → not found");
        return ResultCode.Success;
    }

    /// <summary>命令 11: GetTrustedCertificateCount — [21.0.0+] 获取可信证书计数</summary>
    private ResultCode GetTrustedCertificateCount(IpcRequest request, ref IpcResponse response)
    {
        if (_interfaceVersion < 21)
            return ResultCode.SslResult(6);

        response.Data.AddRange(BitConverter.GetBytes(_trustedCertificateCount));
        Logger.Debug(nameof(SslService), $"ssl: GetTrustedCertificateCount → {_trustedCertificateCount}");
        return ResultCode.Success;
    }

    /// <summary>命令 100: CreateContextForSystem — 为系统进程创建 SSL 上下文</summary>
    private ResultCode CreateContextForSystem(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.SslResult(2);

        uint sslVersion = BitConverter.ToUInt32(request.Data, 0);
        int handle = unchecked((int)_nextContextHandle++);
        _contextCount++;

        Logger.Info(nameof(SslService), $"ssl: CreateContextForSystem(sslVersion={sslVersion}) → handle=0x{handle:X8}");

        response.Data.AddRange(BitConverter.GetBytes(handle));
        return ResultCode.Success;
    }

    /// <summary>外部可查询接口版本</summary>
    public uint InterfaceVersion => _interfaceVersion;

    /// <summary>外部可查询上下文计数</summary>
    public int ContextCount => _contextCount;

    /// <summary>外部可设置可信证书计数（用于测试）</summary>
    public int TrustedCertificateCount
    {
        get => _trustedCertificateCount;
        set => _trustedCertificateCount = value;
    }

    public void Dispose()
    {
        _contextCount = 0;
    }
}
