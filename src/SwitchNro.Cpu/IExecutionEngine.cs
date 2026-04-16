using System;
using SwitchNro.Memory;

namespace SwitchNro.Cpu;

/// <summary>
/// CPU 执行引擎核心接口
/// 支持 Hypervisor 直接执行和 JIT 软件翻译两种模式
/// </summary>
public interface IExecutionEngine : IDisposable
{
    /// <summary>当前执行模式</summary>
    ExecutionMode Mode { get; }

    /// <summary>从入口点开始执行，直到遇到 SVC 或异常</summary>
    ExecutionResult Execute(ulong entryPoint);

    /// <summary>在暂停后继续执行</summary>
    ExecutionResult RunNext();

    /// <summary>暂停执行</summary>
    void Pause();

    /// <summary>请求执行引擎退出</summary>
    void RequestExit();

    /// <summary>获取/设置通用寄存器值</summary>
    ulong GetRegister(int index);
    void SetRegister(int index, ulong value);

    /// <summary>获取/设置程序计数器</summary>
    ulong GetPC();
    void SetPC(ulong value);

    /// <summary>获取/设置栈指针</summary>
    ulong GetSP();
    void SetSP(ulong value);

    /// <summary>获取最近一次 SVC 调用信息</summary>
    SvcInfo GetLastSvcInfo();

    /// <summary>将 SVC 处理结果写回（设置 X0 返回值）</summary>
    void SetSvcResult(ulong returnValue);

    /// <summary>将 SVC 处理结果写回（设置 X0 和 X1 返回值）</summary>
    void SetSvcResult(ulong returnValue0, ulong returnValue1);

    /// <summary>获取 PSTATE 状态寄存器</summary>
    ulong GetPstate();
    void SetPstate(ulong value);

    /// <summary>是否正在执行</summary>
    bool IsRunning { get; }
}

/// <summary>执行模式</summary>
public enum ExecutionMode
{
    /// <summary>Hypervisor.framework 直接执行（ARM64 → ARM64 同构）</summary>
    Hypervisor,

    /// <summary>JIT 软件翻译回退</summary>
    JitFallback,
}
