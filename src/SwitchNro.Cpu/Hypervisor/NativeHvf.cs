using System;
using System.Runtime.InteropServices;

namespace SwitchNro.Cpu.Hypervisor;

/// <summary>
/// macOS Hypervisor.framework P/Invoke 声明
/// 参考: https://developer.apple.com/documentation/hypervisor
/// </summary>
internal static unsafe class NativeHvf
{
    private const string LibName = "/System/Library/Frameworks/Hypervisor.framework/Hypervisor";

    // hv_vm_config_t
    public const ulong HV_VM_DEFAULT = 0;

    // hv_vcpu_exit_reason_t
    public const uint HV_EXIT_REASON_EXCEPTION = 0;
    public const uint HV_EXIT_REASON_VTIMER_ACTIVATED = 1;
    public const uint HV_EXIT_REASON_CANCELED = 2;

    // 异常类型 (EC 值)
    public const uint EC_SVC64 = 0x15;        // SVC 指令执行 (AArch64)
    public const uint EC_BKPT = 0x3C;         // BRK 断点指令
    public const uint EC_DABORT = 0x24;        // 数据异常
    public const uint EC_UNKNOWN = 0x00;       // 未知

    /// <summary>创建虚拟机实例</summary>
    [DllImport(LibName, EntryPoint = "hv_vm_create")]
    public static extern int hv_vm_create(IntPtr config);

    /// <summary>销毁虚拟机实例</summary>
    [DllImport(LibName, EntryPoint = "hv_vm_destroy")]
    public static extern int hv_vm_destroy();

    /// <summary>映射内存区域到虚拟机物理地址空间</summary>
    [DllImport(LibName, EntryPoint = "hv_vm_map")]
    public static extern int hv_vm_map(IntPtr uaddr, ulong gpa, ulong size, ulong flags);

    /// <summary>取消映射</summary>
    [DllImport(LibName, EntryPoint = "hv_vm_unmap")]
    public static extern int hv_vm_unmap(ulong gpa, ulong size);

    /// <summary>创建虚拟 CPU</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_create")]
    public static extern int hv_vcpu_create(out ulong vcpu, IntPtr exit, ulong config);

    /// <summary>销毁虚拟 CPU</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_destroy")]
    public static extern int hv_vcpu_destroy(ulong vcpu);

    /// <summary>执行虚拟 CPU 直到退出</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_run")]
    public static extern int hv_vcpu_run(ulong vcpu);

    /// <summary>获取通用寄存器值</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_get_reg")]
    public static extern int hv_vcpu_get_reg(ulong vcpu, uint reg, out ulong value);

    /// <summary>设置通用寄存器值</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_set_reg")]
    public static extern int hv_vcpu_set_reg(ulong vcpu, uint reg, ulong value);

    /// <summary>获取系统寄存器值</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_get_sys_reg")]
    public static extern int hv_vcpu_get_sys_reg(ulong vcpu, uint reg, out ulong value);

    /// <summary>设置系统寄存器值</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_set_sys_reg")]
    public static extern int hv_vcpu_set_sys_reg(ulong vcpu, uint reg, ulong value);

    /// <summary>获取 FPU SIMD 寄存器 (Q0-Q31)</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_get_simd_reg")]
    public static extern int hv_vcpu_get_simd_reg(ulong vcpu, uint reg, out HvSimdReg value);

    /// <summary>设置 FPU SIMD 寄存器</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_set_simd_reg")]
    public static extern int hv_vcpu_set_simd_reg(ulong vcpu, uint reg, ref HvSimdReg value);

    /// <summary>获取 vCPU 退出信息</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_get_exit")]
    public static extern int hv_vcpu_get_exit(ulong vcpu, out HvVcpuExit exit);

    /// <summary>请求 vCPU 立即退出</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_request_exit")]
    public static extern int hv_vcpu_request_exit(ulong vcpu);

    // 内存映射标志
    public const ulong HV_VM_MAP_READ = 1UL << 0;
    public const ulong HV_VM_MAP_WRITE = 1UL << 1;
    public const ulong HV_VM_MAP_EXECUTE = 1UL << 2;

    // 通用寄存器编号 (X0-X30)
    public const uint REG_X0 = 0;
    public const uint REG_X30 = 30;
    public const uint REG_SP = 31;  // 栈指针在 HVF 中使用特殊编号

    // 系统寄存器
    public const uint SYS_REG_SP_EL0 = 0x00;
    public const uint SYS_REG_SP_EL1 = 0x01;
    public const uint SYS_REG_PC = 0x02;
    public const uint SYS_REG_PSTATE = 0x03;

    /// <summary>SIMD 寄存器结构（128-bit）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HvSimdReg
    {
        public fixed byte Data[16];
    }

    /// <summary>vCPU 退出信息结构</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HvVcpuExit
    {
        public uint Reason;       // hv_vcpu_exit_reason_t
        public uint ExceptionClass; // EC 值
        public ulong FaultVirtualAddress;
        public ulong FaultPhysicalAddress;
        public uint Syndrome;     // ESR 的 ISS 字段
        public uint _padding;
    }
}
