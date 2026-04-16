using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 SPL 服务状态
/// </summary>
public sealed class SplState
{
    /// <summary>是否为开发机</summary>
    private bool _isDevelopment;

    /// <summary>启动原因</summary>
    private uint _bootReason;

    /// <summary>是否为开发机</summary>
    public bool IsDevelopment
    {
        get => _isDevelopment;
        set => _isDevelopment = value;
    }

    /// <summary>启动原因</summary>
    public uint BootReason
    {
        get => _bootReason;
        set => _bootReason = value;
    }
}

/// <summary>
/// SPL 服务基类 — spl:/spl:mig/spl:fs/spl:ssl/spl:es 共享的命令处理逻辑
/// nn::spl::IGeneralInterface / ICryptoInterface / IFsInterface / ISslInterface / IEsInterface
/// 各端口命令表 ID 不同，但底层处理逻辑相同（均为 stub）
/// 命令表基于 SwitchBrew SPL_services 页面
/// </summary>
public abstract class SplServiceBase : IIpcService
{
    public abstract string PortName { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly SplState _state;

    protected SplServiceBase(SplState state)
    {
        _state = state;
        _commandTable = BuildCommandTable();
    }

    /// <summary>子类重写以构建各自端口的命令表</summary>
    /// <remarks>Called from base constructor — safe because overrides only reference base-class protected methods, not subclass fields.</remarks>
    protected abstract Dictionary<uint, ServiceCommand> BuildCommandTable();

    // ── 共享命令处理方法 ──

    /// <summary>GetConfig — 获取配置项 (stub)</summary>
    protected ResultCode GetConfig(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.SplResult(2);
        uint configItem = BitConverter.ToUInt32(request.Data, 0);
        response.Data.AddRange(BitConverter.GetBytes(0UL));
        Logger.Debug(PortName, $"{PortName}: GetConfig(item={configItem}) → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>ModularExponentiate — 模幂运算 (stub)</summary>
    protected ResultCode ModularExponentiate(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ModularExponentiate (stub)");
        return ResultCode.Success;
    }

    /// <summary>GenerateAesKek — 生成 AES KEK (stub)</summary>
    protected ResultCode GenerateAesKek(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[16]);
        Logger.Debug(PortName, $"{PortName}: GenerateAesKek → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>LoadAesKey — 加载 AES 密钥 (stub)</summary>
    protected ResultCode LoadAesKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: LoadAesKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>GenerateAesKey — 生成 AES 密钥 (stub)</summary>
    protected ResultCode GenerateAesKey(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[16]);
        Logger.Debug(PortName, $"{PortName}: GenerateAesKey → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>SetConfig — 设置配置项 (stub)</summary>
    protected ResultCode SetConfig(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: SetConfig (stub)");
        return ResultCode.Success;
    }

    /// <summary>GenerateRandomBytes — 生成随机字节</summary>
    protected ResultCode GenerateRandomBytes(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.SplResult(2);
        uint size = BitConverter.ToUInt32(request.Data, 0);
        size = Math.Min(size, 0x100);
        var random = new byte[size];
        Random.Shared.NextBytes(random);
        response.Data.AddRange(random);
        Logger.Debug(PortName, $"{PortName}: GenerateRandomBytes → {size} bytes");
        return ResultCode.Success;
    }

    /// <summary>IsDevelopment — 是否为开发机</summary>
    protected ResultCode IsDevelopment(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.IsDevelopment ? 1U : 0U));
        Logger.Debug(PortName, $"{PortName}: IsDevelopment → {_state.IsDevelopment}");
        return ResultCode.Success;
    }

    /// <summary>DecryptAndStoreGcKey — 解密并存储 GC 密钥 (stub)</summary>
    protected ResultCode DecryptAndStoreGcKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: DecryptAndStoreGcKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>DecryptGcMessage — 解密 GC 消息 (stub)</summary>
    protected ResultCode DecryptGcMessage(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: DecryptGcMessage (stub)");
        return ResultCode.Success;
    }

    /// <summary>GenerateSpecificAesKey — 生成特定 AES 密钥 (stub)</summary>
    protected ResultCode GenerateSpecificAesKey(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[16]);
        Logger.Debug(PortName, $"{PortName}: GenerateSpecificAesKey → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>DecryptDeviceUniqueData — 解密设备唯一数据 (stub)</summary>
    protected ResultCode DecryptDeviceUniqueData(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: DecryptDeviceUniqueData (stub)");
        return ResultCode.Success;
    }

    /// <summary>DecryptAesKey — 解密 AES 密钥 (stub)</summary>
    protected ResultCode DecryptAesKey(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[16]);
        Logger.Debug(PortName, $"{PortName}: DecryptAesKey → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>ComputeCtr — CTR 模式计算 (stub)</summary>
    protected ResultCode ComputeCtr(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ComputeCtr (stub)");
        return ResultCode.Success;
    }

    /// <summary>ComputeCmac — 计算 CMAC (stub)</summary>
    protected ResultCode ComputeCmac(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[16]);
        Logger.Debug(PortName, $"{PortName}: ComputeCmac → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>LoadEsDeviceKey — 加载 ES 设备密钥 (stub)</summary>
    protected ResultCode LoadEsDeviceKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: LoadEsDeviceKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>PrepareEsTitleKey — 准备 ES 标题密钥 (stub)</summary>
    protected ResultCode PrepareEsTitleKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: PrepareEsTitleKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>LoadPreparedAesKey — 加载已准备的 AES 密钥 (stub)</summary>
    protected ResultCode LoadPreparedAesKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: LoadPreparedAesKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>PrepareCommonEsTitleKey — 准备公共 ES 标题密钥 (stub)</summary>
    protected ResultCode PrepareCommonEsTitleKey(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[16]);
        Logger.Debug(PortName, $"{PortName}: PrepareCommonEsTitleKey → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>AllocateAesKeySlot — 分配 AES 密钥槽</summary>
    protected ResultCode AllocateAesKeySlot(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U));
        Logger.Debug(PortName, $"{PortName}: AllocateAesKeySlot → slot 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>DeallocateAesKeySlot — 释放 AES 密钥槽 (stub)</summary>
    protected ResultCode DeallocateAesKeySlot(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: DeallocateAesKeySlot (stub)");
        return ResultCode.Success;
    }

    /// <summary>GetAesKeySlotAvailableEvent — 获取 AES 密钥槽可用事件 (stub)</summary>
    protected ResultCode GetAesKeySlotAvailableEvent(IpcRequest request, ref IpcResponse response)
    {
        int eventHandle = unchecked((int)0xFFFF0E10);
        response.Data.AddRange(BitConverter.GetBytes(eventHandle));
        Logger.Debug(PortName, $"{PortName}: GetAesKeySlotAvailableEvent → KEvent handle (stub)");
        return ResultCode.Success;
    }

    /// <summary>SetBootReason — 设置启动原因</summary>
    protected ResultCode SetBootReason(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.SplResult(2);
        _state.BootReason = BitConverter.ToUInt32(request.Data, 0);
        Logger.Debug(PortName, $"{PortName}: SetBootReason → {_state.BootReason}");
        return ResultCode.Success;
    }

    /// <summary>GetBootReason — 获取启动原因</summary>
    protected ResultCode GetBootReason(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(_state.BootReason));
        Logger.Debug(PortName, $"{PortName}: GetBootReason → {_state.BootReason}");
        return ResultCode.Success;
    }

    /// <summary>GetConfigWithBuffer — 带缓冲区获取配置 (stub)</summary>
    protected ResultCode GetConfigWithBuffer(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: GetConfigWithBuffer (stub)");
        return ResultCode.Success;
    }

    /// <summary>GetPackage2Hash — 获取 Package2 哈希 (stub)</summary>
    protected ResultCode GetPackage2Hash(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(new byte[0x20]);
        Logger.Debug(PortName, $"{PortName}: GetPackage2Hash → zeros (stub)");
        return ResultCode.Success;
    }

    /// <summary>DecryptAndStoreSslClientCertKey — 解密并存储 SSL 客户端证书密钥 (stub, 5.0.0+)</summary>
    protected ResultCode DecryptAndStoreSslClientCertKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: DecryptAndStoreSslClientCertKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>ModularExponentiateWithSslClientCertKey — SSL 客户端证书模幂 (stub, 5.0.0+)</summary>
    protected ResultCode ModularExponentiateWithSslClientCertKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ModularExponentiateWithSslClientCertKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>DecryptAndStoreDrmDeviceCertKey — 解密并存储 DRM 设备证书密钥 (stub, 5.0.0+)</summary>
    protected ResultCode DecryptAndStoreDrmDeviceCertKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: DecryptAndStoreDrmDeviceCertKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>ModularExponentiateWithDrmDeviceCertKey — DRM 设备证书模幂 (stub, 5.0.0+)</summary>
    protected ResultCode ModularExponentiateWithDrmDeviceCertKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: ModularExponentiateWithDrmDeviceCertKey (stub)");
        return ResultCode.Success;
    }

    /// <summary>PrepareEsArchiveKey — 准备 ES 归档密钥 (stub, 6.0.0+)</summary>
    protected ResultCode PrepareEsArchiveKey(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: PrepareEsArchiveKey (stub)");
        return ResultCode.Success;
    }

    internal SplState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// spl: — 安全平台服务 (通用接口)
/// nn::spl::IGeneralInterface
/// 命令表基于 SwitchBrew SPL_services 页面
/// </summary>
public sealed class SplGeneralService : SplServiceBase
{
    public override string PortName => "spl:";

    public SplGeneralService(SplState state) : base(state) { }

    protected override Dictionary<uint, ServiceCommand> BuildCommandTable() => new()
    {
        [0]  = GetConfig,
        [1]  = ModularExponentiate,
        [2]  = GenerateRandomBytes,
        [5]  = SetConfig,
        [7]  = GenerateRandomBytes,
        [8]  = GenerateAesKek,
        [9]  = LoadAesKey,
        [10] = GenerateAesKey,
        [11] = IsDevelopment,
        [12] = DecryptAndStoreGcKey,
        [13] = DecryptGcMessage,
        [14] = GenerateSpecificAesKey,
        [15] = DecryptDeviceUniqueData,
        [16] = DecryptAesKey,
        [17] = ComputeCtr,
        [18] = ComputeCmac,
        [19] = LoadEsDeviceKey,
        [20] = PrepareEsTitleKey,
        [21] = LoadPreparedAesKey,
        [22] = PrepareCommonEsTitleKey,
        [23] = AllocateAesKeySlot,
        [24] = DeallocateAesKeySlot,
        [25] = GetAesKeySlotAvailableEvent,
        [26] = SetBootReason,
        [27] = GetBootReason,
    };
}

/// <summary>
/// spl:mig — 安全平台服务 (制造接口)
/// nn::spl::ICryptoInterface
/// 命令表基于 SwitchBrew SPL_services 页面
/// </summary>
public sealed class SplMigService : SplServiceBase
{
    public override string PortName => "spl:mig";

    public SplMigService(SplState state) : base(state) { }

    protected override Dictionary<uint, ServiceCommand> BuildCommandTable() => new()
    {
        [0]  = GetConfig,
        [1]  = ModularExponentiate,
        [2]  = GenerateAesKek,
        [3]  = LoadAesKey,
        [4]  = GenerateAesKey,
        [5]  = SetConfig,
        [7]  = GenerateRandomBytes,
        [11] = IsDevelopment,
        [14] = DecryptAesKey,
        [15] = ComputeCtr,
        [16] = ComputeCmac,
        [21] = AllocateAesKeySlot,
        [22] = DeallocateAesKeySlot,
        [23] = GetAesKeySlotAvailableEvent,
        [24] = SetBootReason,
        [25] = GetBootReason,
    };
}

/// <summary>
/// spl:fs — 安全平台服务 (文件系统接口)
/// nn::spl::IFsInterface
/// 命令表基于 SwitchBrew SPL_services 页面
/// </summary>
public sealed class SplFsService : SplServiceBase
{
    public override string PortName => "spl:fs";

    public SplFsService(SplState state) : base(state) { }

    protected override Dictionary<uint, ServiceCommand> BuildCommandTable() => new()
    {
        [0]  = GetConfig,
        [1]  = ModularExponentiate,
        [2]  = GenerateAesKek,
        [3]  = LoadAesKey,
        [4]  = GenerateAesKey,
        [5]  = SetConfig,
        [7]  = GenerateRandomBytes,
        [9]  = DecryptAndStoreGcKey,
        [10] = DecryptGcMessage,
        [11] = IsDevelopment,
        [12] = GenerateSpecificAesKey,
        [19] = LoadPreparedAesKey,
        [31] = GetPackage2Hash,
    };
}

/// <summary>
/// spl:ssl — 安全平台服务 (SSL 接口)
/// nn::spl::ISslInterface
/// 命令表基于 SwitchBrew SPL_services 页面
/// </summary>
public sealed class SplSslService : SplServiceBase
{
    public override string PortName => "spl:ssl";

    public SplSslService(SplState state) : base(state) { }

    protected override Dictionary<uint, ServiceCommand> BuildCommandTable() => new()
    {
        [0]  = GetConfig,
        [1]  = ModularExponentiate,
        [2]  = GenerateAesKek,
        [3]  = LoadAesKey,
        [4]  = GenerateAesKey,
        [5]  = SetConfig,
        [7]  = GenerateRandomBytes,
        [11] = IsDevelopment,
        [13] = DecryptDeviceUniqueData,
        [14] = DecryptAesKey,
        [15] = ComputeCtr,
        [16] = ComputeCmac,
        [21] = AllocateAesKeySlot,
        [22] = DeallocateAesKeySlot,
        [23] = GetAesKeySlotAvailableEvent,
        [24] = SetBootReason,
        [25] = GetBootReason,
        [26] = DecryptAndStoreSslClientCertKey,
        [27] = ModularExponentiateWithSslClientCertKey,
    };
}

/// <summary>
/// spl:es — 安全平台服务 (ES 接口)
/// nn::spl::IEsInterface
/// 命令表基于 SwitchBrew SPL_services 页面
/// </summary>
public sealed class SplEsService : SplServiceBase
{
    public override string PortName => "spl:es";

    public SplEsService(SplState state) : base(state) { }

    protected override Dictionary<uint, ServiceCommand> BuildCommandTable() => new()
    {
        [0]  = GetConfig,
        [1]  = ModularExponentiate,
        [2]  = GenerateAesKek,
        [3]  = LoadAesKey,
        [4]  = GenerateAesKey,
        [5]  = SetConfig,
        [7]  = GenerateRandomBytes,
        [11] = IsDevelopment,
        [13] = DecryptDeviceUniqueData,
        [14] = DecryptAesKey,
        [15] = ComputeCtr,
        [16] = ComputeCmac,
        [17] = LoadEsDeviceKey,
        [18] = PrepareEsTitleKey,
        [20] = PrepareCommonEsTitleKey,
        [21] = AllocateAesKeySlot,
        [22] = DeallocateAesKeySlot,
        [23] = GetAesKeySlotAvailableEvent,
        [24] = SetBootReason,
        [25] = GetBootReason,
        [28] = DecryptAndStoreDrmDeviceCertKey,
        [29] = ModularExponentiateWithDrmDeviceCertKey,
        [31] = PrepareEsArchiveKey,
        [32] = LoadPreparedAesKey,
    };
}
