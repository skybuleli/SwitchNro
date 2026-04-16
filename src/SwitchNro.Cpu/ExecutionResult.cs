namespace SwitchNro.Cpu;

/// <summary>CPU 执行引擎的退出原因</summary>
public enum ExecutionResult
{
    /// <summary>遇到 SVC 系统调用，需 HLE 处理</summary>
    SVC,

    /// <summary>触发断点</summary>
    Breakpoint,

    /// <summary>内存访问异常</summary>
    MemoryFault,

    /// <summary>未定义指令</summary>
    UndefinedInstruction,

    /// <summary>正常退出（程序结束）</summary>
    NormalExit,

    /// <summary>执行超时</summary>
    Timeout,

    /// <summary>调试暂停</summary>
    DebugPause,
}
