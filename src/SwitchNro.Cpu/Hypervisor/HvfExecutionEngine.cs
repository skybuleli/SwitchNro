using System;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Memory;

namespace SwitchNro.Cpu.Hypervisor;

/// <summary>
/// Hypervisor.framework 执行引擎
/// 利用 macOS HVF 将 NRO 的 ARM64 代码直接映射到虚拟 CPU 执行
/// </summary>
public sealed class HvfExecutionEngine : IExecutionEngine
{
    private ulong _vcpu;
    private bool _vmCreated;
    private bool _isRunning;
    private bool _exitRequested;
    private SvcInfo _lastSvc;
    private readonly VirtualMemoryManager _memory;

    public ExecutionMode Mode => ExecutionMode.Hypervisor;
    public bool IsRunning => _isRunning;

    public HvfExecutionEngine(VirtualMemoryManager memory)
    {
        _memory = memory;
        Initialize();
    }

    private void Initialize()
    {
        // 创建虚拟机
        int ret = NativeHvf.hv_vm_create(IntPtr.Zero);
        if (ret != 0)
        {
            Logger.Error(nameof(HvfExecutionEngine), $"hv_vm_create 失败: {ret}");
            throw new InvalidOperationException($"Hypervisor 初始化失败: hv_vm_create 返回 {ret}");
        }
        _vmCreated = true;

        // 创建虚拟 CPU
        ret = NativeHvf.hv_vcpu_create(out _vcpu, IntPtr.Zero, 0);
        if (ret != 0)
        {
            Logger.Error(nameof(HvfExecutionEngine), $"hv_vcpu_create 失败: {ret}");
            throw new InvalidOperationException($"vCPU 创建失败: hv_vcpu_create 返回 {ret}");
        }

        Logger.Info(nameof(HvfExecutionEngine), "Hypervisor 执行引擎初始化完成");
    }

    /// <summary>将虚拟内存页映射到 HVF 物理地址空间</summary>
    public void MapMemoryToHvf(ulong gpa, ulong size, MemoryPermissions perms)
    {
        ulong hvfPerms = 0;
        if ((perms & MemoryPermissions.Read) != 0) hvfPerms |= NativeHvf.HV_VM_MAP_READ;
        if ((perms & MemoryPermissions.Write) != 0) hvfPerms |= NativeHvf.HV_VM_MAP_WRITE;
        if ((perms & MemoryPermissions.Execute) != 0) hvfPerms |= NativeHvf.HV_VM_MAP_EXECUTE;

        var hostPtr = _memory.GetPhysicalAddress(gpa);
        if (hostPtr == IntPtr.Zero)
        {
            Logger.Error(nameof(HvfExecutionEngine), $"无法获取 0x{gpa:X16} 的物理地址");
            return;
        }

        int ret = NativeHvf.hv_vm_map(hostPtr, gpa, size, hvfPerms);
        if (ret != 0)
        {
            Logger.Error(nameof(HvfExecutionEngine), $"hv_vm_map 失败: GPA=0x{gpa:X16} Size=0x{size:X16} ret={ret}");
        }
    }

    public ExecutionResult Execute(ulong entryPoint)
    {
        SetPC(entryPoint);
        return RunLoop();
    }

    public ExecutionResult RunNext()
    {
        return RunLoop();
    }

    private ExecutionResult RunLoop()
    {
        _isRunning = true;
        _exitRequested = false;

        try
        {
            while (!_exitRequested)
            {
                int ret = NativeHvf.hv_vcpu_run(_vcpu);
                if (ret != 0)
                {
                    Logger.Error(nameof(HvfExecutionEngine), $"hv_vcpu_run 失败: {ret}");
                    return ExecutionResult.NormalExit;
                }

                var exitInfo = new NativeHvf.HvVcpuExit();
                ret = NativeHvf.hv_vcpu_get_exit(_vcpu, out exitInfo);
                if (ret != 0)
                {
                    Logger.Error(nameof(HvfExecutionEngine), $"hv_vcpu_get_exit 失败: {ret}");
                    return ExecutionResult.NormalExit;
                }

                var result = HandleExit(exitInfo);
                if (result != ExecutionResult.SVC)
                    return result;

                // SVC 由调用方处理后调用 Continue
                return result;
            }

            return ExecutionResult.NormalExit;
        }
        finally
        {
            _isRunning = false;
        }
    }

    private ExecutionResult HandleExit(NativeHvf.HvVcpuExit exit)
    {
        switch (exit.Reason)
        {
            case NativeHvf.HV_EXIT_REASON_EXCEPTION:
                return HandleException(exit);

            case NativeHvf.HV_EXIT_REASON_CANCELED:
                return ExecutionResult.DebugPause;

            case NativeHvf.HV_EXIT_REASON_VTIMER_ACTIVATED:
                // 定时器中断，继续执行
                return RunNext();

            default:
                Logger.Warning(nameof(HvfExecutionEngine), $"未知退出原因: {exit.Reason}");
                return ExecutionResult.NormalExit;
        }
    }

    private ExecutionResult HandleException(NativeHvf.HvVcpuExit exit)
    {
        switch (exit.ExceptionClass)
        {
            case NativeHvf.EC_SVC64:
                // SVC 系统调用
                var svcNumber = exit.Syndrome & 0xFFFF; // ISS 低 16 位为 SVC 编号
                _lastSvc = new SvcInfo
                {
                    SvcNumber = svcNumber,
                    X0 = GetRegister(0),
                    X1 = GetRegister(1),
                    X2 = GetRegister(2),
                    X3 = GetRegister(3),
                    X4 = GetRegister(4),
                    X5 = GetRegister(5),
                    X6 = GetRegister(6),
                    X7 = GetRegister(7),
                    PC = GetPC(),
                    SP = GetSP(),
                };
                Logger.Debug(nameof(HvfExecutionEngine), $"SVC 拦截: {_lastSvc}");
                return ExecutionResult.SVC;

            case NativeHvf.EC_BKPT:
                Logger.Info(nameof(HvfExecutionEngine), $"断点触发: PC=0x{GetPC():X16}");
                return ExecutionResult.Breakpoint;

            case NativeHvf.EC_DABORT:
                Logger.Warning(nameof(HvfExecutionEngine), $"数据异常: VA=0x{exit.FaultVirtualAddress:X16}");
                return ExecutionResult.MemoryFault;

            default:
                Logger.Error(nameof(HvfExecutionEngine), $"未处理异常: EC=0x{exit.ExceptionClass:X2}");
                return ExecutionResult.UndefinedInstruction;
        }
    }

    public void Pause()
    {
        _ = NativeHvf.hv_vcpu_request_exit(_vcpu);
    }
    public void RequestExit() => _exitRequested = true;

    public ulong GetRegister(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 31);
        _ = NativeHvf.hv_vcpu_get_reg(_vcpu, (uint)index, out var value);
        return value;
    }

    public void SetRegister(int index, ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 31);
        _ = NativeHvf.hv_vcpu_set_reg(_vcpu, (uint)index, value);
    }

    public ulong GetPC()
    {
        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_PC, out var value);
        return value;
    }

    public void SetPC(ulong value)
    {
        _ = NativeHvf.hv_vcpu_set_sys_reg(_vcpu, NativeHvf.SYS_REG_PC, value);
    }

    public ulong GetSP()
    {
        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_SP_EL0, out var value);
        return value;
    }

    public void SetSP(ulong value)
    {
        _ = NativeHvf.hv_vcpu_set_sys_reg(_vcpu, NativeHvf.SYS_REG_SP_EL0, value);
    }

    public SvcInfo GetLastSvcInfo() => _lastSvc;

    public void SetSvcResult(ulong returnValue)
    {
        SetRegister(0, returnValue);
    }

    public void SetSvcResult(ulong returnValue0, ulong returnValue1)
    {
        SetRegister(0, returnValue0);
        SetRegister(1, returnValue1);
    }

    public ulong GetPstate()
    {
        _ = NativeHvf.hv_vcpu_get_sys_reg(_vcpu, NativeHvf.SYS_REG_PSTATE, out var value);
        return value;
    }

    public void SetPstate(ulong value)
    {
        _ = NativeHvf.hv_vcpu_set_sys_reg(_vcpu, NativeHvf.SYS_REG_PSTATE, value);
    }

    public void Dispose()
    {
        if (_vcpu != 0)
        {
            _ = NativeHvf.hv_vcpu_destroy(_vcpu);
            _vcpu = 0;
        }

        if (_vmCreated)
        {
            _ = NativeHvf.hv_vm_destroy();
            _vmCreated = false;
        }

        Logger.Info(nameof(HvfExecutionEngine), "Hypervisor 执行引擎已释放");
    }
}
