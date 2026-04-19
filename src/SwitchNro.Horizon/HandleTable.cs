using System;
using System.Collections.Generic;
using SwitchNro.Common.Logging;

namespace SwitchNro.Horizon;

/// <summary>
/// 内核对象基类
/// 表示可通过句柄引用的 Horizon 内核对象
/// </summary>
public abstract class KObject
{
    /// <summary>对象类型标识</summary>
    public abstract string ObjectType { get; }
}

/// <summary>
/// 可等待的内核对象接口
/// WaitSynchronization 只能等待实现了此接口的对象
/// 包括: KEvent, KReadableEvent, KClientSession 等
/// </summary>
public interface IWaitable
{
    /// <summary>对象是否已信号（可以被 WaitSynchronization 捕获）</summary>
    bool IsSignaled { get; }
}

/// <summary>
/// 客户端会话内核对象
/// ConnectToNamedPort 创建此对象，关联一个命名的 IPC 服务端口
/// </summary>
public sealed class KClientSession : KObject, IWaitable
{
    /// <summary>关联的服务端口名称（如 "sm:", "fs:"）</summary>
    public string ServicePortName { get; }

    public override string ObjectType => "ClientSession";

    /// <summary>会话是否可等待 — 在 HLE 模拟中，IPC 会话始终视为已信号（同步 IPC 模型）</summary>
    public bool IsSignaled => true;

    public KClientSession(string servicePortName)
    {
        ServicePortName = servicePortName;
    }
}

/// <summary>
/// 内核事件对象
/// 用于 WaitSynchronization 等同步操作
/// </summary>
public sealed class KEvent : KObject, IWaitable
{
    /// <summary>事件是否已信号</summary>
    public bool IsSignaled { get; set; }

    public override string ObjectType => "KEvent";

    public KEvent(bool signaled = false)
    {
        IsSignaled = signaled;
    }
}

/// <summary>
/// 线程活动状态（用于 SVC 0x40 SetThreadActivity）
/// 对应 Horizon 内核 ThreadActivity 枚举
/// </summary>
public enum ThreadActivity
{
    /// <summary>线程可被调度执行（默认状态）</summary>
    Runnable = 0,
    /// <summary>线程被暂停，不会被调度</summary>
    Paused = 1,
}

/// <summary>
/// 线程状态（对应 Horizon 内核 KThread 的运行时状态）
/// </summary>
public enum ThreadState
{
    /// <summary>线程已创建但尚未启动（StartThread 未调用）</summary>
    Created,
    /// <summary>线程正在运行或可被调度</summary>
    Running,
    /// <summary>线程被暂停（SetThreadActivity Paused），不会被调度</summary>
    Paused,
    /// <summary>线程已终止（正常退出或被杀死）</summary>
    Terminated,
}

/// <summary>
/// 内核线程对象
/// 表示进程中可调度的执行线程
/// 通过 Homebrew ABI 传递给 NRO 作为主线程句柄
/// 也可由 SVC 0x34 CreateThread 创建
/// </summary>
public sealed class KThread : KObject, IWaitable
{
    /// <summary>线程 ID</summary>
    public ulong ThreadId { get; }

    /// <summary>线程入口函数地址</summary>
    public ulong EntryPoint { get; }

    /// <summary>传递给入口函数的参数</summary>
    public ulong Argument { get; }

    /// <summary>栈顶地址（ARM64 栈向下增长，此值为栈区域末尾）</summary>
    public ulong StackTop { get; }

    /// <summary>栈区域基地址（用于释放时取消映射）</summary>
    public ulong StackBase { get; }

    /// <summary>栈大小（字节）</summary>
    public ulong StackSize { get; }

    /// <summary>线程优先级 (0-63，0 最低)</summary>
    public int Priority { get; set; }

    /// <summary>处理器亲和性核心 ID (0-3)，-2 = 任意核心</summary>
    public int ProcessorId { get; }

    /// <summary>线程的 TLS 区域基地址（每个线程 0x200 字节）</summary>
    public ulong TlsAddress { get; set; }

    /// <summary>线程当前运行状态</summary>
    public ThreadState State { get; set; } = ThreadState.Created;

    public override string ObjectType => "KThread";

    /// <summary>
    /// 线程是否可等待
    /// 真实 Horizon: KThread 句柄在线程终止时变为信号状态（用于 Join 语义）
    /// 当前单线程模型: 主线程始终视为已信号（简化 WaitSynchronization）
    /// CreateThread 创建的线程: 终止后变为已信号
    /// TODO: 多线程实现后，IsSignaled 应在线程终止时才为 true
    /// </summary>
    public bool IsSignaled => State == ThreadState.Terminated || _isMainThread;

    private readonly bool _isMainThread;

    /// <summary>
    /// 创建主线程 KThread（StartProcess 使用）
    /// 主线程始终视为已信号（简化 WaitSynchronization）
    /// </summary>
    public KThread(ulong threadId)
    {
        ThreadId = threadId;
        _isMainThread = true;
        State = ThreadState.Running;
        // 主线程的入口/栈/优先级等在 StartProcess 中通过 Engine 直接设置
        EntryPoint = 0;
        Argument = 0;
        StackTop = 0;
        StackBase = 0;
        StackSize = 0;
        Priority = 44; // 标准应用主线程优先级
        ProcessorId = 0;
    }

    /// <summary>
    /// 创建新线程 KThread（SVC 0x34 CreateThread 使用）
    /// </summary>
    public KThread(ulong threadId, ulong entryPoint, ulong argument, ulong stackTop,
        ulong stackBase, ulong stackSize, int priority, int processorId)
    {
        ThreadId = threadId;
        _isMainThread = false;
        State = ThreadState.Created;
        EntryPoint = entryPoint;
        Argument = argument;
        StackTop = stackTop;
        StackBase = stackBase;
        StackSize = stackSize;
        Priority = priority;
        ProcessorId = processorId;
    }
}

/// <summary>
/// 内核可等待事件对象（KEvent + 可读事件句柄的组合）
/// 部分 HLE 服务返回 KEvent 的可读事件句柄
/// </summary>
public sealed class KReadableEvent : KObject, IWaitable
{
    /// <summary>是否已信号</summary>
    public bool IsSignaled { get; set; }

    public override string ObjectType => "ReadableEvent";

    public KReadableEvent(bool signaled = false)
    {
        IsSignaled = signaled;
    }
}

/// <summary>
/// 进程内核句柄表
/// 管理进程内所有的内核对象句柄映射
/// 句柄 ID 从 0xD000 开始递增（与 Horizon OS 行为一致）
/// </summary>
public sealed class HandleTable
{
    private readonly Dictionary<int, KObject> _handles = new();
    private int _nextHandle = 0xD000;

    /// <summary>当前有效句柄数</summary>
    public int Count => _handles.Count;

    /// <summary>
    /// 创建新句柄并注册内核对象
    /// </summary>
    /// <param name="obj">要注册的内核对象</param>
    /// <returns>分配的句柄 ID</returns>
    public int CreateHandle(KObject obj)
    {
        int handle = _nextHandle++;
        _handles[handle] = obj;
        Logger.Debug(nameof(HandleTable), $"创建句柄 0x{handle:X8} → {obj.ObjectType}");
        return handle;
    }

    /// <summary>
    /// 获取句柄对应的内核对象
    /// </summary>
    /// <param name="handle">句柄 ID</param>
    /// <returns>内核对象，如果句柄无效则返回 null</returns>
    public KObject? GetObject(int handle)
    {
        return _handles.TryGetValue(handle, out var obj) ? obj : null;
    }

    /// <summary>
    /// 获取句柄对应的特定类型内核对象
    /// </summary>
    /// <typeparam name="T">期望的内核对象类型</typeparam>
    /// <param name="handle">句柄 ID</param>
    /// <returns>类型匹配的内核对象，如果句柄无效或类型不匹配则返回 null</returns>
    public T? GetObject<T>(int handle) where T : KObject
    {
        return GetObject(handle) as T;
    }

    /// <summary>
    /// 关闭句柄并移除内核对象引用
    /// </summary>
    /// <param name="handle">要关闭的句柄 ID</param>
    /// <returns>是否成功关闭</returns>
    public bool CloseHandle(int handle)
    {
        if (!_handles.Remove(handle))
        {
            Logger.Warning(nameof(HandleTable), $"关闭无效句柄: 0x{handle:X8}");
            return false;
        }

        Logger.Debug(nameof(HandleTable), $"关闭句柄 0x{handle:X8}");
        return true;
    }

    /// <summary>
    /// 检查句柄是否有效
    /// </summary>
    public bool IsValid(int handle) => _handles.ContainsKey(handle);

    /// <summary>
    /// 清空所有句柄
    /// </summary>
    public void Clear()
    {
        _handles.Clear();
    }
}
