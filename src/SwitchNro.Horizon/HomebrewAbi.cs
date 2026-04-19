using System;
using System.Runtime.InteropServices;
using SwitchNro.Common.Logging;
using SwitchNro.Memory;

namespace SwitchNro.Horizon;

// ──────────────────── Homebrew ABI 常量 ────────────────────

/// <summary>
/// Homebrew ABI 配置条目类型（EntryType）
/// 与 libnx switch/runtime/env.h 中的定义一致
/// </summary>
public static class HbEntryType
{
    /// <summary>条目列表结束标记</summary>
    public const uint EndOfList = 0;
    /// <summary>主线程句柄 — Value[0] = 线程句柄 (u64)</summary>
    public const uint MainThreadHandle = 1;
    /// <summary>下一个加载路径 — Value[0] = 路径字符串指针</summary>
    public const uint NextLoadPath = 2;
    /// <summary>堆大小覆盖 — Value[0] = 堆大小 (u64), Value[1] = 堆地址 (如果适用)</summary>
    public const uint OverrideHeap = 3;
    /// <summary>服务覆盖 — Value[0] = 服务名称/覆盖指针</summary>
    public const uint OverrideService = 4;
    /// <summary>命令行参数</summary>
    public const uint Argv = 5;
    /// <summary>系统调用可用性提示</summary>
    public const uint SyscallAvailableHint = 6;
    /// <summary>Applet 类型</summary>
    public const uint AppletType = 7;
    /// <summary>ApmStateAndTev</summary>
    public const uint ApmStateAndTev = 8;
    /// <summary>随机种子</summary>
    public const uint RandomSeed = 9;
    /// <summary>HostConnection</summary>
    public const uint HostConnection = 10;
    /// <summary>HOS 版本 — Value[0] = 版本号 (u64, 格式: major<<16 | minor<<8 | micro)</summary>
    public const uint HosVersion = 11;
}

/// <summary>
/// Homebrew ABI 配置条目标志位
/// </summary>
public static class HbConfigFlags
{
    /// <summary>无标志</summary>
    public const uint None = 0;
    /// <summary>此条目是必需的，缺失时 libnx envInitialize() 应失败</summary>
    public const uint Mandatory = 1;
}

// ──────────────────── ConfigEntry 结构体 ────────────────────

/// <summary>
/// Homebrew ABI 配置条目 (40 字节)
/// 布局: Key(u32) + Flags(u32) + Value[4](u64×4)
/// 注意：在 AArch64 上，Value 数组会自动对齐到 8 字节边界。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HbConfigEntry
{
    public uint Key;
    public uint Flags;
    public ulong Value0;
    public ulong Value1;
    public ulong Value2;
    public ulong Value3;

    public static int StructSize => 40; // 4+4 + 8*4
}

// ──────────────────── LoaderConfig 构建器 ────────────────────

/// <summary>
/// Homebrew ABI loader_config 构建器
/// 在 guest 内存中构建 ConfigEntry 数组，供 NRO 入口点读取
/// 
/// 调用约定:
///   X0 = loader_config 结构指针
///   X1 = 0xFFFFFFFFFFFFFFFF (哨兵值，标识 Homebrew ABI 模式)
/// </summary>
public sealed class HomebrewLoaderConfig
{
    private readonly VirtualMemoryManager _memory;
    private readonly List<HbConfigEntry> _entries = new();

    /// <summary>loader_config 在 guest 内存中的基地址</summary>
    public ulong BaseAddress { get; private set; }

    /// <summary>loader_config 总大小（字节）</summary>
    public int TotalSize => _entries.Count * HbConfigEntry.StructSize;

    public HomebrewLoaderConfig(VirtualMemoryManager memory)
    {
        _memory = memory;
    }

    /// <summary>添加主线程句柄条目</summary>
    public HomebrewLoaderConfig AddMainThreadHandle(int threadHandle)
    {
        _entries.Add(new HbConfigEntry
        {
            Key = HbEntryType.MainThreadHandle,
            Flags = HbConfigFlags.Mandatory,
            Value0 = (ulong)threadHandle,
        });
        return this;
    }

    /// <summary>添加堆大小条目</summary>
    public HomebrewLoaderConfig AddHeapSize(ulong heapSize)
    {
        _entries.Add(new HbConfigEntry
        {
            Key = HbEntryType.OverrideHeap,
            Flags = HbConfigFlags.Mandatory,
            Value0 = heapSize,
            Value1 = 0,
        });
        return this;
    }

    /// <summary>
    /// 添加系统版本条目
    /// </summary>
    /// <param name="major">系统版本主号 (如 18)</param>
    /// <param name="minor">系统版本次号 (如 1)</param>
    /// <param name="micro">系统版本微号 (如 0)</param>
    public HomebrewLoaderConfig AddSystemVersion(byte major, byte minor, byte micro)
    {
        // Horizon 系统版本编码: (major << 16) | (minor << 8) | micro
        ulong version = ((ulong)major << 16) | ((ulong)minor << 8) | micro;
        _entries.Add(new HbConfigEntry
        {
            Key = HbEntryType.HosVersion,
            Flags = HbConfigFlags.Mandatory,
            Value0 = version,
        });
        return this;
    }

    /// <summary>添加 Applet 类型条目</summary>
    public HomebrewLoaderConfig AddAppletType(uint appletType)
    {
        _entries.Add(new HbConfigEntry
        {
            Key = HbEntryType.AppletType,
            Flags = 0,
            Value0 = (ulong)appletType,
        });
        return this;
    }

    /// <summary>添加系统调用可用性提示</summary>
    public HomebrewLoaderConfig AddSyscallAvailableHint(ulong mask0, ulong mask1)
    {
        _entries.Add(new HbConfigEntry
        {
            Key = HbEntryType.SyscallAvailableHint,
            Flags = 0,
            Value0 = mask0,
            Value1 = mask1,
        });
        return this;
    }

    /// <summary>添加随机种子条目</summary>
    public HomebrewLoaderConfig AddRandomSeed(ulong seed0, ulong seed1)
    {
        _entries.Add(new HbConfigEntry
        {
            Key = HbEntryType.RandomSeed,
            Flags = 0,
            Value0 = seed0,
            Value1 = seed1,
        });
        return this;
    }

    /// <summary>
    /// 写入 loader_config 到 guest 内存
    /// 自动添加 EndOfList 终止条目，并在指定基地址分配内存
    /// </summary>
    /// <param name="baseAddress">loader_config 在 guest 内存中的基地址</param>
    /// <returns>基地址（即传入的 baseAddress）</returns>
    public ulong WriteToMemory(ulong baseAddress)
    {
        BaseAddress = baseAddress;

        // 添加 EndOfList 终止条目
        var allEntries = new List<HbConfigEntry>(_entries)
        {
            new HbConfigEntry
            {
                Key = HbEntryType.EndOfList,
                Flags = 0,
            }
        };

        int totalSize = allEntries.Count * HbConfigEntry.StructSize;

        // 映射内存页（RW 权限，供 guest 读取）
        // 如果页已映射（可能与其他分配重叠），跳过映射
        ulong alignedAddr = baseAddress & ~0xFFFUL; // 4KB 对齐 (VMM 页粒度)
        ulong endAddr = (baseAddress + (ulong)totalSize + 0xFFFUL) & ~0xFFFUL;
        ulong mapSize = endAddr - alignedAddr;

        if (!_memory.IsMapped(alignedAddr))
        {
            _memory.MapZero(alignedAddr, mapSize, MemoryPermissions.ReadWrite, MemoryType.Normal);
        }

        // 逐条写入 ConfigEntry
        ulong offset = 0;
        foreach (var entry in allEntries)
        {
            _memory.Write(baseAddress + offset, entry);
            offset += (ulong)HbConfigEntry.StructSize;
        }

        Logger.Info(nameof(HomebrewLoaderConfig),
            $"loader_config 写入 0x{baseAddress:X16}, {_entries.Count} 个条目 + EndOfList, " +
            $"总大小 {totalSize} 字节");

        return baseAddress;
    }
}
