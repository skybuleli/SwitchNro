using System;
using SwitchNro.Cpu;
using SwitchNro.Memory;

namespace SwitchNro.Debugger;

/// <summary>
/// 调试器服务
/// 统一入口：断点管理、内存查看、寄存器查看、GPU Profiler
/// </summary>
public sealed class DebuggerService : IDisposable
{
    /// <summary>断点管理器</summary>
    public BreakpointManager Breakpoints { get; } = new();

    /// <summary>关联的执行引擎</summary>
    private IExecutionEngine? _engine;

    /// <summary>关联的虚拟内存管理器</summary>
    private VirtualMemoryManager? _memory;

    /// <summary>是否处于调试暂停状态</summary>
    public bool IsPaused { get; private set; }

    /// <summary>绑定执行引擎和内存管理器</summary>
    public void Attach(IExecutionEngine engine, VirtualMemoryManager memory)
    {
        _engine = engine;
        _memory = memory;
        Breakpoints.BreakpointHit += OnBreakpointHit;
    }

    /// <summary>单步执行</summary>
    public void Step()
    {
        if (_engine == null) return;
        _engine.RunNext();
    }

    /// <summary>继续执行</summary>
    public void Continue()
    {
        IsPaused = false;
        _engine?.RunNext();
    }

    /// <summary>暂停执行</summary>
    public void Pause()
    {
        IsPaused = true;
        _engine?.Pause();
    }

    /// <summary>读取寄存器值</summary>
    public ulong GetRegister(int index) => _engine?.GetRegister(index) ?? 0;

    /// <summary>读取程序计数器</summary>
    public ulong GetPC() => _engine?.GetPC() ?? 0;

    /// <summary>读取栈指针</summary>
    public ulong GetSP() => _engine?.GetSP() ?? 0;

    /// <summary>读取内存区域（十六进制视图用）</summary>
    public byte[] ReadMemory(ulong address, int length)
    {
        if (_memory == null) return [];
        var data = new byte[length];
        _memory.Read(address, data);
        return data;
    }

    /// <summary>写入内存</summary>
    public void WriteMemory(ulong address, byte[] data)
    {
        _memory?.Write(address, data);
    }

    private void OnBreakpointHit(BreakpointHit hit)
    {
        IsPaused = true;
        _engine?.Pause();
    }

    public void Dispose()
    {
        Breakpoints.BreakpointHit -= OnBreakpointHit;
    }
}
