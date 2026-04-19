using System;

namespace SwitchNro.Horizon;

/// <summary>Horizon OS 进程信息</summary>
public sealed record ProcessInfo
{
    /// <summary>进程 ID</summary>
    public ulong ProcessId { get; init; }

    /// <summary>进程标题 ID</summary>
    public ulong TitleId { get; init; }

    /// <summary>进程名称</summary>
    public string Name { get; init; } = "";

    /// <summary>进程创建时间</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>进程状态</summary>
    public ProcessState State { get; set; } = ProcessState.Created;

    /// <summary>入口点地址</summary>
    public ulong EntryPoint { get; init; }

    /// <summary>主线程栈大小</summary>
    public ulong MainStackSize { get; init; } = 0x100000; // 1MB 默认

    /// <summary>进程类别</summary>
    public ProcessCategory Category { get; init; } = ProcessCategory.Application;
}

/// <summary>进程状态</summary>
public enum ProcessState
{
    Created,
    Running,
    Paused,
    Exiting,
    Exited,
    Crashed,
}

/// <summary>进程类别</summary>
public enum ProcessCategory
{
    Application,
    Applet,
    System,
}
