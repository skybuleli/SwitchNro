using System;
using System.Collections.Generic;
using System.Linq;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.HLE.Ipc;
using SwitchNro.Horizon;

namespace SwitchNro.HLE.Services;

/// <summary>
/// ldr:shel — 加载器 Shell 服务 (Loader Shell Interface)
/// 设置程序启动参数、刷新参数缓冲区
/// Homebrew 启动时通过此服务传递 argv 参数
/// </summary>
public sealed class LdrShelService : IIpcService
{
    public string PortName => "ldr:shel";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>已存储的程序启动参数 (ProgramId → 参数字符串)</summary>
    private readonly Dictionary<ulong, string> _programArguments = new();

    public LdrShelService()
    {
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = SetProgramArgument,   // 设置程序启动参数
            [1] = FlushArguments,       // 刷新参数缓冲区
        };
    }

    /// <summary>命令 0: SetProgramArgument — 设置指定程序的启动参数</summary>
    private ResultCode SetProgramArgument(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16)
            return ResultCode.LdrResult(2); // Invalid size

        // 输入布局: u32 size + padding(4) + u64 programId + argument string
        uint argSize = BitConverter.ToUInt32(request.Data, 0);
        ulong programId = BitConverter.ToUInt64(request.Data, 8);

        string args = "";
        if (request.Data.Length > 16 && argSize > 0)
        {
            int stringLen = Math.Min((int)argSize, request.Data.Length - 16);
            args = System.Text.Encoding.UTF8.GetString(request.Data, 16, stringLen).TrimEnd('\0');
        }

        _programArguments[programId] = args;
        Logger.Info(nameof(LdrShelService), $"ldr:shel: SetProgramArgument(programId=0x{programId:X16}, size={argSize}, args=\"{args}\")");

        return ResultCode.Success;
    }

    /// <summary>命令 1: FlushArguments — 刷新参数缓冲区</summary>
    private ResultCode FlushArguments(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(LdrShelService), "ldr:shel: FlushArguments");
        // 将内存中的参数提交到目标进程
        return ResultCode.Success;
    }

    /// <summary>获取指定程序的启动参数（供其他服务查询）</summary>
    public string? GetProgramArgument(ulong programId) =>
        _programArguments.TryGetValue(programId, out var args) ? args : null;

    public void Dispose() { }
}

/// <summary>
/// ldr:dmnt — 加载器调试监控服务 (Loader Debug Monitor Interface)
/// 提供调试版的参数设置和进程模块信息查询
/// 调试器通过此服务获取已加载模块的地址和大小
/// </summary>
public sealed class LdrDmntService : IIpcService
{
    public string PortName => "ldr:dmnt";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>关联的 HorizonSystem 引用（用于查询进程模块信息）</summary>
    private readonly HorizonSystem? _system;

    /// <summary>已存储的程序启动参数（调试版，与 ldr:shel 共享逻辑）</summary>
    private readonly Dictionary<ulong, string> _programArguments = new();

    public LdrDmntService(HorizonSystem? system = null)
    {
        _system = system;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = SetProgramArgument2,   // 设置程序启动参数 (调试版)
            [1] = FlushArguments2,       // 刷新参数缓冲区 (调试版)
            [2] = GetProcessModuleInfo,  // 获取进程模块信息
        };
    }

    /// <summary>命令 0: SetProgramArgument2 — 设置程序启动参数（调试接口）</summary>
    private ResultCode SetProgramArgument2(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 16)
            return ResultCode.LdrResult(2);

        uint argSize = BitConverter.ToUInt32(request.Data, 0);
        ulong programId = BitConverter.ToUInt64(request.Data, 8);

        string args = "";
        if (request.Data.Length > 16 && argSize > 0)
        {
            int stringLen = Math.Min((int)argSize, request.Data.Length - 16);
            args = System.Text.Encoding.UTF8.GetString(request.Data, 16, stringLen).TrimEnd('\0');
        }

        _programArguments[programId] = args;
        Logger.Info(nameof(LdrDmntService), $"ldr:dmnt: SetProgramArgument2(programId=0x{programId:X16}, args=\"{args}\")");

        return ResultCode.Success;
    }

    /// <summary>命令 1: FlushArguments2 — 刷新参数缓冲区（调试接口）</summary>
    private ResultCode FlushArguments2(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(LdrDmntService), "ldr:dmnt: FlushArguments2");
        return ResultCode.Success;
    }

    /// <summary>命令 2: GetProcessModuleInfo — 获取进程模块信息</summary>
    private ResultCode GetProcessModuleInfo(IpcRequest request, ref IpcResponse response)
    {
        // 输入布局: u64 pid + s32 index (12 bytes minimum)
        if (request.Data.Length < 12)
            return ResultCode.LdrResult(2);

        ulong pid = BitConverter.ToUInt64(request.Data, 0);
        int index = BitConverter.ToInt32(request.Data, 8);
        Logger.Debug(nameof(LdrDmntService), $"ldr:dmnt: GetProcessModuleInfo(PID={pid}, index={index})");

        if (_system != null)
        {
            var process = _system.GetAllProcesses().FirstOrDefault(p => p.Info.ProcessId == pid);
            if (process != null && index == 0) // 目前仅支持主模块 (index=0)
            {
                // 返回 ModuleInfo 结构: ModuleId(0x20) + Address(0x8) + Size(0x8) = 0x30 bytes
                var moduleId = new byte[0x20]; // 虚拟 ModuleId (全零)
                response.Data.AddRange(moduleId);
                response.Data.AddRange(BitConverter.GetBytes(process.NroModule.TextSegment.Address)); // 模块地址
                response.Data.AddRange(BitConverter.GetBytes((ulong)process.NroModule.Header.TextSize +
                                                             process.NroModule.Header.RodataSize +
                                                             process.NroModule.Header.DataSize));     // 模块大小

                // 返回模块数量 = 1
                response.Data.AddRange(BitConverter.GetBytes(1)); // s32 count
                return ResultCode.Success;
            }
        }

        // 无匹配进程或 index 越界，返回 0 个模块
        response.Data.AddRange(BitConverter.GetBytes(0)); // s32 count = 0
        return ResultCode.Success;
    }

    /// <summary>获取指定程序的启动参数（供调试器查询）</summary>
    public string? GetProgramArgument(ulong programId) =>
        _programArguments.TryGetValue(programId, out var args) ? args : null;

    public void Dispose() { }
}

/// <summary>
/// ldr:pm — 加载器进程管理服务 (Loader Process Manager Interface)
/// 提供进程创建、程序信息查询、程序 Pin/Unpin 操作
/// 系统启动流程中的关键服务，PM 模块通过此服务创建进程
/// </summary>
public sealed class LdrPmService : IIpcService
{
    public string PortName => "ldr:pm";

    public IReadOnlyDictionary<uint, ServiceCommand> CommandTable => _commandTable;

    private readonly Dictionary<uint, ServiceCommand> _commandTable;

    /// <summary>关联的 HorizonSystem 引用</summary>
    private readonly HorizonSystem? _system;

    /// <summary>已 Pin 的程序 (PinId → ProgramId)</summary>
    private readonly Dictionary<ulong, ulong> _pinnedPrograms = new();

    /// <summary>下一个 PinId 计数器</summary>
    private ulong _nextPinId = 1;

    /// <summary>是否启用程序验证</summary>
    private bool _programVerificationEnabled = true;

    public LdrPmService(HorizonSystem? system = null)
    {
        _system = system;
        _commandTable = new Dictionary<uint, ServiceCommand>
        {
            [0] = CreateProcess,                   // 创建进程
            [1] = GetProgramInfo,                  // 获取程序信息
            [2] = PinProgram,                      // Pin 程序
            [3] = UnpinProgram,                    // Unpin 程序
            [4] = SetEnabledProgramVerification,   // [10.0.0+] 设置程序验证开关
        };
    }

    /// <summary>命令 0: CreateProcess — 创建进程</summary>
    private ResultCode CreateProcess(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 24)
            return ResultCode.LdrResult(2);

        ulong pinId = BitConverter.ToUInt64(request.Data, 0);
        uint createProcessFlags = BitConverter.ToUInt32(request.Data, 8);
        // ResourceLimit handle 在 offset 12 处（4 字节），此处忽略

        Logger.Info(nameof(LdrPmService), $"ldr:pm: CreateProcess(pinId={pinId}, flags=0x{createProcessFlags:X8})");

        // 如果没有已 Pin 的程序，返回错误
        if (!_pinnedPrograms.ContainsKey(pinId))
        {
            Logger.Warning(nameof(LdrPmService), $"ldr:pm: 未找到 PinId={pinId} 对应的已 Pin 程序");
            return ResultCode.LdrResult(6); // Not pinned
        }

        // 返回虚拟进程句柄（限制 pinId 范围避免溢出）
        int virtualHandle = unchecked((int)(0xFFFF1000U + (uint)(pinId & 0xFFFU)));
        response.Data.AddRange(BitConverter.GetBytes(virtualHandle));

        return ResultCode.Success;
    }

    /// <summary>命令 1: GetProgramInfo — 获取程序信息</summary>
    private ResultCode GetProgramInfo(IpcRequest request, ref IpcResponse response)
    {
        Logger.Debug(nameof(LdrPmService), "ldr:pm: GetProgramInfo");

        // ProgramInfo 结构:
        //   MainThreadPriority(1) + DefaultCpuId(1) + Flags(2) + MainThreadStackSize(4) +
        //   ProgramId(8) + AcidSacSize(4) + AciSacSize(4) + AcidFacSize(4) + AciFacSize(4) = 0x20 bytes
        // 后续还有变长 SAC/FAC 数据

        // 填充默认 ProgramInfo (Homebrew 标准值)
        response.Data.Add(44);        // MainThreadPriority = 44 (标准应用优先级)
        response.Data.Add(0);          // DefaultCpuId = 0 (Core 0)
        response.Data.AddRange(BitConverter.GetBytes((ushort)0)); // Flags
        response.Data.AddRange(BitConverter.GetBytes(0x100000U)); // MainThreadStackSize = 1MB
        response.Data.AddRange(BitConverter.GetBytes(0UL));       // ProgramId (占位)

        // SAC/FAC sizes (全零 — 无特别访问控制)
        response.Data.AddRange(BitConverter.GetBytes(0)); // AcidSacSize
        response.Data.AddRange(BitConverter.GetBytes(0)); // AciSacSize
        response.Data.AddRange(BitConverter.GetBytes(0)); // AcidFacSize
        response.Data.AddRange(BitConverter.GetBytes(0)); // AciFacSize

        return ResultCode.Success;
    }

    /// <summary>命令 2: PinProgram — Pin 指定程序（准备创建进程）</summary>
    private ResultCode PinProgram(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.LdrResult(2);

        ulong programId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Info(nameof(LdrPmService), $"ldr:pm: PinProgram(programId=0x{programId:X16})");

        ulong pinId = _nextPinId++;
        _pinnedPrograms[pinId] = programId;

        response.Data.AddRange(BitConverter.GetBytes(pinId));
        return ResultCode.Success;
    }

    /// <summary>命令 3: UnpinProgram — Unpin 指定程序</summary>
    private ResultCode UnpinProgram(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 8)
            return ResultCode.LdrResult(2);

        ulong pinId = BitConverter.ToUInt64(request.Data, 0);
        Logger.Info(nameof(LdrPmService), $"ldr:pm: UnpinProgram(pinId={pinId})");

        if (!_pinnedPrograms.Remove(pinId))
        {
            Logger.Warning(nameof(LdrPmService), $"ldr:pm: 未找到 PinId={pinId}");
            return ResultCode.LdrResult(6);
        }

        return ResultCode.Success;
    }

    /// <summary>命令 4: SetEnabledProgramVerification — [10.0.0+] 设置程序验证开关</summary>
    private ResultCode SetEnabledProgramVerification(IpcRequest request, ref IpcResponse response)
    {
        if (request.Data.Length < 1)
            return ResultCode.LdrResult(2);

        bool enabled = request.Data[0] != 0;
        _programVerificationEnabled = enabled;
        Logger.Info(nameof(LdrPmService), $"ldr:pm: SetEnabledProgramVerification → {enabled}");

        return ResultCode.Success;
    }

    /// <summary>查询程序验证是否启用（外部查询用）</summary>
    public bool IsProgramVerificationEnabled => _programVerificationEnabled;

    public void Dispose() { }
}
