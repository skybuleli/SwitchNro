using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;

namespace SwitchNro.HLE.Services;

/// <summary>
/// 共享的 MM 服务状态
/// </summary>
public sealed class MmState
{
    /// <summary>已注册的会话列表</summary>
    private readonly List<MmSession> _sessions = new();

    /// <summary>下一个会话句柄</summary>
    private uint _nextSessionHandle = 0xFFFF0C00;

    /// <summary>获取所有会话</summary>
    public IReadOnlyList<MmSession> Sessions => _sessions;

    /// <summary>创建新会话</summary>
    public MmSession CreateSession(string module)
    {
        var session = new MmSession
        {
            Handle = unchecked((int)_nextSessionHandle++),
            Module = module,
            Initialized = false,
            Waiting = false,
            Value = 0,
        };
        _sessions.Add(session);
        return session;
    }

    /// <summary>按句柄查找会话</summary>
    public MmSession? FindSession(int handle)
    {
        foreach (var s in _sessions)
            if (s.Handle == handle) return s;
        return null;
    }
}

/// <summary>MM 会话</summary>
public sealed class MmSession
{
    public int Handle { get; set; }
    public string Module { get; set; } = "";
    public bool Initialized { get; set; }
    public bool Waiting { get; set; }
    public uint Value { get; set; }
}

/// <summary>
/// MM 服务基类 — mm:u/mm:sv 共享的命令处理逻辑
/// nn::mm::detail::ISession
/// 两个端口命令表完全相同，仅端口名不同
/// 命令表基于 SwitchBrew MM_services 页面
/// </summary>
public abstract class MmServiceBase : IIpcService
{
    public abstract string PortName { get; }

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    private readonly MmState _state;

    protected MmServiceBase(MmState state)
    {
        _state = state;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = InitializeOld,          // 初始化会话 (旧版)
            [1] = FinalizeOld,            // 终结会话 (旧版)
            [2] = SetAndWaitOld,           // 设置并等待 (旧版, stub)
            [3] = GetOld,                  // 获取值 (旧版)
            [4] = Initialize,             // 初始化会话
            [5] = Finalize,               // 终结会话
            [6] = SetAndWait,             // 设置并等待 (stub)
            [7] = Get,                    // 获取值
        };
    }

    /// <summary>命令 0: InitializeOld — 初始化会话 (旧版)</summary>
    private ResultCode InitializeOld(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.MmResult(2);
        uint module = BitConverter.ToUInt32(request.Data, 0);
        var session = _state.CreateSession($"module_{module}");
        session.Initialized = true;
        response.Data.AddRange(BitConverter.GetBytes(session.Handle));
        Logger.Debug(PortName, $"{PortName}: InitializeOld(module={module}) → handle=0x{session.Handle:X8}");
        return ResultCode.Success;
    }

    /// <summary>命令 1: FinalizeOld — 终结会话 (旧版)</summary>
    private ResultCode FinalizeOld(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: FinalizeOld (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 2: SetAndWaitOld — 设置并等待 (旧版, stub)</summary>
    private ResultCode SetAndWaitOld(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: SetAndWaitOld (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 3: GetOld — 获取值 (旧版)</summary>
    private ResultCode GetOld(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // value = 0
        Logger.Debug(PortName, $"{PortName}: GetOld → 0 (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 4: Initialize — 初始化会话</summary>
    private ResultCode Initialize(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 4) return ResultCode.MmResult(2);
        uint module = BitConverter.ToUInt32(request.Data, 0);
        var session = _state.CreateSession($"module_{module}");
        session.Initialized = true;
        response.Data.AddRange(BitConverter.GetBytes(session.Handle));
        Logger.Debug(PortName, $"{PortName}: Initialize(module={module}) → handle=0x{session.Handle:X8}");
        return ResultCode.Success;
    }

    /// <summary>命令 5: Finalize — 终结会话</summary>
    private ResultCode Finalize(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: Finalize (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 6: SetAndWait — 设置并等待 (stub)</summary>
    private ResultCode SetAndWait(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(PortName, $"{PortName}: SetAndWait (stub)");
        return ResultCode.Success;
    }

    /// <summary>命令 7: Get — 获取值</summary>
    private ResultCode Get(IpcRequest request, ref IpcResponse response)
    {
        response.Data.AddRange(BitConverter.GetBytes(0U)); // value = 0
        Logger.Debug(PortName, $"{PortName}: Get → 0 (stub)");
        return ResultCode.Success;
    }

    internal MmState State => _state;

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>mm:u — 内存监控服务 (用户端口)</summary>
public sealed class MmUService : MmServiceBase
{
    public override string PortName => "mm:u";
    public MmUService(MmState state) : base(state) { }
}

/// <summary>mm:sv — 内存监控服务 (服务器端口)</summary>
public sealed class MmSvService : MmServiceBase
{
    public override string PortName => "mm:sv";
    public MmSvService(MmState state) : base(state) { }
}
