using System;
using System.Collections.Generic;
using SwitchNro.Common.Logging;

namespace SwitchNro.Debugger;

/// <summary>
/// 断点管理器
/// 支持地址断点、条件断点和内存访问断点
/// </summary>
public sealed class BreakpointManager
{
    private readonly Dictionary<ulong, Breakpoint> _breakpoints = new();
    private int _nextBreakpointId = 1;

    /// <summary>断点触发事件</summary>
    public event Action<BreakpointHit>? BreakpointHit;

    /// <summary>添加地址断点</summary>
    public Breakpoint AddBreakpoint(ulong address, BreakpointType type = BreakpointType.Execute)
    {
        var bp = new Breakpoint
        {
            Id = _nextBreakpointId++,
            Address = address,
            Type = type,
            IsEnabled = true,
        };
        _breakpoints[address] = bp;
        Logger.Info(nameof(BreakpointManager), $"添加断点 #{bp.Id}: 0x{address:X16} [{type}]");
        return bp;
    }

    /// <summary>添加条件断点</summary>
    public Breakpoint AddConditionalBreakpoint(ulong address, string condition)
    {
        var bp = AddBreakpoint(address, BreakpointType.Conditional);
        bp.Condition = condition;
        return bp;
    }

    /// <summary>添加内存访问断点</summary>
    public Breakpoint AddMemoryBreakpoint(ulong address, ulong size, MemoryAccessType access)
    {
        var bp = AddBreakpoint(address, BreakpointType.MemoryAccess);
        bp.Size = size;
        bp.AccessType = access;
        return bp;
    }

    /// <summary>移除断点</summary>
    public void RemoveBreakpoint(int id)
    {
        foreach (var kv in _breakpoints)
        {
            if (kv.Value.Id == id)
            {
                _breakpoints.Remove(kv.Key);
                Logger.Info(nameof(BreakpointManager), $"移除断点 #{id}");
                return;
            }
        }
    }

    /// <summary>检查给定地址是否命中断点</summary>
    public bool CheckBreakpoint(ulong address, MemoryAccessType access = MemoryAccessType.Execute)
    {
        if (_breakpoints.TryGetValue(address, out var bp) && bp.IsEnabled)
        {
            if (bp.Type == BreakpointType.Execute && access == MemoryAccessType.Execute)
            {
                BreakpointHit?.Invoke(new BreakpointHit { Breakpoint = bp, Address = address });
                return true;
            }
        }
        return false;
    }

    /// <summary>获取所有断点</summary>
    public IReadOnlyCollection<Breakpoint> GetAllBreakpoints() => _breakpoints.Values;
}

/// <summary>断点信息</summary>
public sealed class Breakpoint
{
    public int Id { get; init; }
    public ulong Address { get; init; }
    public BreakpointType Type { get; init; }
    public bool IsEnabled { get; set; }
    public string? Condition { get; set; }
    public ulong Size { get; set; }
    public MemoryAccessType AccessType { get; set; }
}

/// <summary>断点类型</summary>
public enum BreakpointType
{
    Execute,
    Conditional,
    MemoryAccess,
}

/// <summary>内存访问类型</summary>
[Flags]
public enum MemoryAccessType
{
    Read = 1,
    Write = 2,
    Execute = 4,
}

/// <summary>断点命中信息</summary>
public readonly struct BreakpointHit
{
    public Breakpoint Breakpoint { get; init; }
    public ulong Address { get; init; }
}
