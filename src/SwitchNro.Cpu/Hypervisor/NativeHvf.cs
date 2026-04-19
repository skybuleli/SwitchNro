using System;
using System.Runtime.InteropServices;

namespace SwitchNro.Cpu.Hypervisor;

/// <summary>
/// macOS Hypervisor.framework P/Invoke 声明
/// 参考: https://developer.apple.com/documentation/hypervisor
/// 
/// 重要 ARM64 注意事项:
/// - hv_vcpu_create 的 exit 参数是 hv_vcpu_exit_t** 双重指针，
///   Apple Silicon 上必须传入有效地址以接收退出结构体指针
/// - hv_vcpu_get_exit 仅在 x86_64 上存在，ARM64 上不导出
/// - hv_vm_map 要求 GPA/size/uaddr 均 16KB 对齐（Apple Silicon 页大小）
/// </summary>
internal static unsafe class NativeHvf
{
    private const string LibName = "/System/Library/Frameworks/Hypervisor.framework/Hypervisor";

    // hv_vm_config_t
    public const ulong HV_VM_DEFAULT = 0;

    // hv_vcpu_exit_reason_t （注意：Apple 头文件中枚举顺序为 CANCELED=0, EXCEPTION=1, VTIMER=2）
    public const uint HV_EXIT_REASON_CANCELED = 0;
    public const uint HV_EXIT_REASON_EXCEPTION = 1;
    public const uint HV_EXIT_REASON_VTIMER_ACTIVATED = 2;

    // 异常类型 (EC 值)
    public const uint EC_SVC64 = 0x15;        // SVC 指令执行 (AArch64)
    public const uint EC_SMC64 = 0x17;        // SMC 指令执行 (AArch64)
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
    /// <remarks>Apple Silicon 要求 uaddr, gpa, size 均 16KB 对齐</remarks>
    [DllImport(LibName, EntryPoint = "hv_vm_map")]
    public static extern int hv_vm_map(IntPtr uaddr, ulong gpa, ulong size, ulong flags);

    /// <summary>取消映射</summary>
    [DllImport(LibName, EntryPoint = "hv_vm_unmap")]
    public static extern int hv_vm_unmap(ulong gpa, ulong size);

    /// <summary>
    /// 创建虚拟 CPU
    /// ARM64 签名: hv_return_t hv_vcpu_create(hv_vcpu_t *vcpu, hv_vcpu_exit_t **exit, hv_vcpu_config_t config)
    /// exit 是双重指针：HVF 会分配退出结构体并将指针写入 *exit
    /// 传入 NULL 会导致 SIGSEGV（HVF 尝试写入地址 0x0）
    /// </summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_create")]
    public static extern int hv_vcpu_create(out ulong vcpu, out IntPtr exit, ulong config);

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

    // 注意: hv_vcpu_get_exit 仅在 x86_64 上存在，ARM64 上不导出
    // ARM64 上应使用 hv_vcpu_create 返回的 exit 指针直接读取退出结构体

    /// <summary>启用或禁用对 Debug Exception 的陷入（Trap）</summary>
    [DllImport(LibName, EntryPoint = "hv_vcpu_set_trap_debug_exceptions")]
    public static extern int hv_vcpu_set_trap_debug_exceptions(ulong vcpu, [MarshalAs(UnmanagedType.I1)] bool value);
    /// <summary>请求 vCPU 立即退出（可跨线程调用）</summary>
    /// <remarks>
    /// ARM64 Apple Silicon 上的正确 API 是 hv_vcpus_exit(hv_vcpu_t *vcpus, uint32_t count)
    /// 注意是 vcpuS（复数），接受 vcpu 数组指针和数量
    /// </remarks>
    [DllImport(LibName, EntryPoint = "hv_vcpus_exit")]
    public static extern unsafe int hv_vcpus_exit(ulong* vcpus, uint vcpu_count);

    // 内存映射标志
    public const ulong HV_VM_MAP_READ = 1UL << 0;
    public const ulong HV_VM_MAP_WRITE = 1UL << 1;
    public const ulong HV_VM_MAP_EXECUTE = 1UL << 2;

    // 通用寄存器编号 (hv_reg_t: X0-X30, PC, FPCR, FPSR, CPSR)
    public const uint REG_X0 = 0;
    public const uint REG_X30 = 30;
    public const uint REG_PC = 31;     // HV_REG_PC: PC 是通用寄存器，不是系统寄存器！
    public const uint REG_FPCR = 32;   // HV_REG_FPCR
    public const uint REG_FPSR = 33;   // HV_REG_FPSR
    public const uint REG_CPSR = 34;   // HV_REG_CPSR (即 PSTATE)

    // 系统寄存器 (hv_sys_reg_t: 通过 hv_vcpu_get/set_sys_reg 访问)
    public const uint SYS_REG_ELR_EL1 = 0xc201;      // HV_SYS_REG_ELR_EL1
    public const uint SYS_REG_ESR_EL1 = 0xc290;      // HV_SYS_REG_ESR_EL1
    public const uint SYS_REG_SPSR_EL1 = 0xc200;     // HV_SYS_REG_SPSR_EL1
    public const uint SYS_REG_VBAR_EL1 = 0xc600;     // HV_SYS_REG_VBAR_EL1
    public const uint SYS_REG_FAR_EL1 = 0xc300;      // HV_SYS_REG_FAR_EL1
    public const uint SYS_REG_SP_EL0 = 0xc208;       // HV_SYS_REG_SP_EL0
    public const uint SYS_REG_SP_EL1 = 0xe208;       // HV_SYS_REG_SP_EL1
    public const uint SYS_REG_TPIDR_EL0 = 0xde82;    // HV_SYS_REG_TPIDR_EL0
    public const uint SYS_REG_TPIDR_EL1 = 0xc684;    // HV_SYS_REG_TPIDR_EL1
    public const uint SYS_REG_TPIDRRO_EL0 = 0xde83;  // HV_SYS_REG_TPIDRRO_EL0

    /// <summary>SIMD 寄存器结构（128-bit）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HvSimdReg
    {
        public fixed byte Data[16];
    }

    /// <summary>
    /// vCPU 退出信息结构（Apple Silicon ARM64 版本）
    /// 
    /// 布局来自 Apple hv_vcpu_types.h:
    /// 
    /// typedef struct {
    ///     uint64_t syndrome;         // hv_exception_syndrome_t = ESR_EL2
    ///     uint64_t virtual_address;  // hv_exception_address_t  = FAR_EL2
    ///     uint64_t physical_address; // hv_ipa_t
    /// } hv_vcpu_exit_exception_t;
    /// 
    /// typedef struct {
    ///     uint32_t reason;           // hv_exit_reason_t
    ///     // 4 bytes padding (自然对齐到 8 字节边界)
    ///     hv_vcpu_exit_exception_t exception;
    /// } hv_vcpu_exit_t;
    /// 
    /// 总大小: 4 + 4(pad) + 8 + 8 + 8 = 32 字节
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HvVcpuExit
    {
        public uint Reason;                  // +0x00: hv_exit_reason_t (uint32)
        public uint _Padding;                // +0x04: 自然对齐填充
        public ulong Syndrome;               // +0x08: hv_exception_syndrome_t (uint64 = ESR_EL2 完整值)
        public ulong FaultVirtualAddress;    // +0x10: hv_exception_address_t (uint64 = FAR_EL2)
        public ulong FaultPhysicalAddress;   // +0x18: hv_ipa_t (uint64)

        /// <summary>从 Syndrome 提取 EC (Exception Class, bits [31:26])</summary>
        public uint ExceptionClass => (uint)((Syndrome >> 26) & 0x3F);

        /// <summary>从 Syndrome 提取 ISS (Instruction Specific Syndrome, bits [24:0])</summary>
        public uint Iss => (uint)(Syndrome & 0x1FFFFFF);
    }
}
