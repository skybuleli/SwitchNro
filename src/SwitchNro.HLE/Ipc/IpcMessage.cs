using System;
using SwitchNro.Common;
using SwitchNro.Memory;

namespace SwitchNro.HLE.Ipc;

/// <summary>
/// IPC 消息头 (CMIF 格式，8 字节 = 2 × uint32)
/// 遵循 Horizon OS 的 IPC 二进制协议格式
/// 
/// Word0 (偏移 0x00):
///   bits [0:15]   — Type (命令类型，含 IpcCommandType)
///   bits [16:19]  — PtrBuffCount (X 指针缓冲区描述符数量)
///   bits [20:23]  — SendBuffCount (A 发送缓冲区描述符数量)
///   bits [24:27]  — RecvBuffCount (B 接收缓冲区描述符数量)
///   bits [28:31]  — XchgBuffCount (W 交换缓冲区描述符数量)
/// 
/// Word1 (偏移 0x04):
///   bits [0:9]    — RawDataSize (数据载荷大小，以 4 字节为单位)
///   bits [10:13]  — RecvListFlags (C 接收列表描述符标志)
///   bits [14:30]  — Reserved
///   bits [31]     — HndDescEnable (句柄描述符使能位)
/// </summary>
public readonly struct IpcMessageHeader
{
    /// <summary>Word0 原始值</summary>
    public uint Word0 { get; }

    /// <summary>Word1 原始值</summary>
    public uint Word1 { get; }

    public IpcMessageHeader(uint word0, uint word1)
    {
        Word0 = word0;
        Word1 = word1;
    }

    // ──────────────────── Word0 字段 ────────────────────

    /// <summary>命令类型 (Word0 bits [0:15])</summary>
    public IpcCommandType CommandType => (IpcCommandType)(Word0 & 0xFFFF);

    /// <summary>X (指针) 缓冲区描述符数量 (Word0 bits [16:19])</summary>
    public int PtrBuffCount => (int)((Word0 >> 16) & 0xF);

    /// <summary>A (发送) 缓冲区描述符数量 (Word0 bits [20:23])</summary>
    public int SendBuffCount => (int)((Word0 >> 20) & 0xF);

    /// <summary>B (接收) 缓冲区描述符数量 (Word0 bits [24:27])</summary>
    public int RecvBuffCount => (int)((Word0 >> 24) & 0xF);

    /// <summary>W (交换) 缓冲区描述符数量 (Word0 bits [28:31])</summary>
    public int XchgBuffCount => (int)((Word0 >> 28) & 0xF);

    // ──────────────────── Word1 字段 ────────────────────

    /// <summary>数据载荷大小 (Word1 bits [0:9]，以 4 字节为单位)</summary>
    public int RawDataSizeWords => (int)(Word1 & 0x3FF);

    /// <summary>数据载荷大小 (字节单位)</summary>
    public int RawDataSize => RawDataSizeWords * 4;

    /// <summary>C 接收列表描述符标志 (Word1 bits [10:13])</summary>
    public int RecvListFlags => (int)((Word1 >> 10) & 0xF);

    /// <summary>句柄描述符使能 (Word1 bit [31])</summary>
    public bool HndDescEnable => (Word1 & 0x80000000u) != 0;

    // ──────────────────── TIPC 兼容 ────────────────────

    /// <summary>
    /// TIPC 格式命令 ID (仅对 TIPC 有效)
    /// TIPC header: Type[0:15] | CommandId[16:31] (单 word)
    /// </summary>
    public uint TipcCommandId => (Word0 >> 16) & 0xFFFF;

    /// <summary>
    /// TIPC 格式数据字数 (仅对 TIPC 有效)
    /// </summary>
    public int TipcDataSizeWords => (int)((Word0 >> 16) & 0xFFFF);

    // ──────────────────── 向后兼容属性 ────────────────────

    /// <summary>X 描述符数量 (PtrBuffCount 的别名)</summary>
    public int XDescriptorCount => PtrBuffCount;

    /// <summary>A 描述符数量 (SendBuffCount 的别名)</summary>
    public int ADescriptorCount => SendBuffCount;

    /// <summary>B 描述符数量 (RecvBuffCount 的别名)</summary>
    public int BDescriptorCount => RecvBuffCount;

    /// <summary>W 描述符数量 (XchgBuffCount 的别名)</summary>
    public int WDescriptorCount => XchgBuffCount;
}

/// <summary>IPC 命令类型</summary>
public enum IpcCommandType : uint
{
    Invalid = 0,
    LegacyControl = 1,
    LegacyRequest = 2,
    Close = 4,
    Request = 5,
    Control = 6,
    RequestWithContext = 7,
    ControlWithContext = 8,
    TipcClose = 9,
    TipcRequest = 10,
    TipcControl = 11,
}

// ──────────────────── IPC 句柄描述符 ────────────────────

/// <summary>
/// IPC 句柄描述符
/// 当 IpcMessageHeader.HndDescEnable 为 true 时，紧跟在 CMIF 头部之后
/// 
/// 布局:
///   uint32 word: HasPId[0] | CopyHandleCount[1:4] | MoveHandleCount[5:8]
///   如果 HasPId: ulong PId (8 字节)
///   Copy handles: uint32[] (每个 4 字节)
///   Move handles: uint32[] (每个 4 字节)
/// </summary>
public sealed class IpcHandleDesc
{
    /// <summary>是否包含进程 ID</summary>
    public bool HasPId { get; }

    /// <summary>客户端进程 ID</summary>
    public ulong PId { get; }

    /// <summary>Copy 句柄列表</summary>
    public int[] CopyHandles { get; }

    /// <summary>Move 句柄列表</summary>
    public int[] MoveHandles { get; }

    public IpcHandleDesc(int[] copyHandles, int[] moveHandles, ulong pid = 0, bool hasPid = false)
    {
        CopyHandles = copyHandles;
        MoveHandles = moveHandles;
        PId = pid;
        HasPId = hasPid;
    }

    /// <summary>
    /// 从缓冲区解析句柄描述符
    /// </summary>
    /// <param name="buffer">IPC 缓冲区</param>
    /// <param name="offset">起始偏移</param>
    /// <returns>解析后的句柄描述符和消耗的字节数</returns>
    public static (IpcHandleDesc desc, int bytesRead) Parse(byte[] buffer, int offset)
    {
        if (offset + 4 > buffer.Length)
            return (new IpcHandleDesc([], [], 0, false), 0);

        uint word = BitConverter.ToUInt32(buffer, offset);
        offset += 4;

        bool hasPid = (word & 1) != 0;
        int copyCount = (int)((word >> 1) & 0xF);
        int moveCount = (int)((word >> 5) & 0xF);

        ulong pid = 0;
        if (hasPid && offset + 8 <= buffer.Length)
        {
            pid = BitConverter.ToUInt64(buffer, offset);
            offset += 8;
        }
        else if (hasPid)
        {
            offset += 8; // 跳过即使不完整
        }

        var copyHandles = new int[copyCount];
        for (int i = 0; i < copyCount && offset + 4 <= buffer.Length; i++)
        {
            copyHandles[i] = BitConverter.ToInt32(buffer, offset);
            offset += 4;
        }

        var moveHandles = new int[moveCount];
        for (int i = 0; i < moveCount && offset + 4 <= buffer.Length; i++)
        {
            moveHandles[i] = BitConverter.ToInt32(buffer, offset);
            offset += 4;
        }

        return (new IpcHandleDesc(copyHandles, moveHandles, pid, hasPid), GetSize(hasPid, copyCount, moveCount));
    }

    /// <summary>计算句柄描述符的总字节大小</summary>
    public static int GetSize(bool hasPid, int copyCount, int moveCount)
    {
        return 4 + (hasPid ? 8 : 0) + copyCount * 4 + moveCount * 4;
    }

    /// <summary>计算当前句柄描述符的总字节大小</summary>
    public int Size => GetSize(HasPId, CopyHandles.Length, MoveHandles.Length);

    /// <summary>
    /// 将句柄描述符写入缓冲区
    /// </summary>
    public void Write(byte[] buffer, int offset)
    {
        uint word = HasPId ? 1u : 0u;
        word |= (uint)(CopyHandles.Length & 0xF) << 1;
        word |= (uint)(MoveHandles.Length & 0xF) << 5;
        BitConverter.TryWriteBytes(buffer.AsSpan(offset), word);
        offset += 4;

        if (HasPId)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), PId);
            offset += 8;
        }

        foreach (var h in CopyHandles)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), h);
            offset += 4;
        }

        foreach (var h in MoveHandles)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), h);
            offset += 4;
        }
    }
}

// ──────────────────── IPC 缓冲区描述符 ────────────────────

/// <summary>
/// 缓冲区方向/类型
/// 对应 Horizon IPC 协议中 A/B/W/X/C 描述符类型
/// </summary>
public enum IpcBufferType
{
    /// <summary>A 类型 — 发送缓冲区 (客户端→服务端)</summary>
    Send = 0,
    /// <summary>B 类型 — 接收缓冲区 (服务端→客户端)</summary>
    Receive = 1,
    /// <summary>W 类型 — 交换缓冲区 (双向)</summary>
    Exchange = 2,
    /// <summary>X 类型 — 指针缓冲区 (服务端内联数据)</summary>
    PointerBuffer = 3,
    /// <summary>C 类型 — 接收列表缓冲区</summary>
    ReceiveList = 4,
}

/// <summary>
/// IPC 缓冲区描述符 (A/B/W 类型，12 字节 = 3 × uint32)
/// 遵循 Horizon OS IPC 二进制协议（与 Ryujinx IpcBuffDesc 一致）
/// 
/// 布局 (3 × uint32, 12 字节):
///   Word0: Size 低位 (32 位，低 32 位字节大小)
///   Word1: Position 低位 (32 位，低 32 位地址)
///   Word2: 混合字段 (与 Ryujinx IpcBuffDesc 一致)
///     bits [0:1]   — Flags (属性标志)
///     bits [2:4]   — Position[36:38]  (word2<<34 → Position 高位)
///     bits [24:27] — Size[32:35]      (word2<<8  → Size 高位)
///     bits [28:31] — Position[32:35]  (word2<<4  → Position 高位)
/// </summary>
public readonly struct IpcBuffDesc
{
    /// <summary>缓冲区地址 (Position)</summary>
    public ulong Address { get; }

    /// <summary>缓冲区大小 (字节单位)</summary>
    public ulong Size { get; }

    /// <summary>属性标志 (2 位)</summary>
    public byte Flags { get; }

    public IpcBuffDesc(ulong address, ulong size, byte flags)
    {
        Address = address;
        Size = size;
        Flags = flags;
    }

    /// <summary>
    /// 从 3 个 uint32 字解析 IpcBuffDesc
    /// </summary>
    public static IpcBuffDesc FromWords(uint word0, uint word1, uint word2)
    {
        ulong position = word1;
        position |= ((ulong)word2 << 4) & 0x0f_0000_0000ul;
        position |= ((ulong)word2 << 34) & 0x07_0000_0000_0ul;

        ulong size = word0;
        size |= ((ulong)word2 << 8) & 0x0f_0000_0000ul;

        byte flags = (byte)(word2 & 3);

        return new IpcBuffDesc(position, size, flags);
    }

    /// <summary>
    /// 将描述符编码回 3 个 uint32 字
    /// </summary>
    public (uint word0, uint word1, uint word2) ToWords()
    {
        uint word0 = (uint)Size;
        uint word1 = (uint)Address;

        uint word2 = Flags;
        word2 |= (uint)((Address & 0x0f_0000_0000ul) >> 4);
        word2 |= (uint)((Size & 0x0f_0000_0000ul) >> 8);
        word2 |= (uint)((Address & 0x07_0000_0000_0ul) >> 34);

        return (word0, word1, word2);
    }

    /// <summary>从组件创建描述符</summary>
    public static IpcBuffDesc Create(ulong address, ulong size, int flags)
        => new(address, size, (byte)(flags & 3));

    public override string ToString() => $"Addr=0x{Address:X16} Size=0x{Size:X} Flags=0x{Flags:X}";
}

/// <summary>
/// IPC 指针缓冲区描述符 (X 类型，8 字节 = 2 × uint32)
/// 遵循 Horizon OS IPC 二进制协议（与 Ryujinx IpcPtrBuffDesc 一致）
/// 
/// 布局 (2 × uint32, 8 字节):
///   Word0:
///     bits [0:5]   — Index 低位 (6 位)
///     bits [6:8]   — Index 高位 (3 位, 实际是 [9:11] 左移)
///     bits [16:31] — Size (16 位无符号, 最大 0xFFFF = 64KB-1)
///     bits [20:23] — Position[32:35] → Position |= (word0 << 20) & 0x0f00000000
///     bits [30:31] — Position[36:37] → Position |= (word0 << 30) & 0x7000000000
///   Word1:
///     bits [0:31]  — Position 低位 (32 位地址)
/// </summary>
public readonly struct IpcPtrBuffDesc
{
    /// <summary>缓冲区地址 (Position)</summary>
    public ulong Address { get; }

    /// <summary>缓冲区索引 (用于标识这是第几个 C 描述符的映射)</summary>
    public uint Index { get; }

    /// <summary>缓冲区大小 (16 位, 最大 0xFFFF)</summary>
    public ulong Size { get; }

    public IpcPtrBuffDesc(ulong address, uint index, ulong size)
    {
        Address = address;
        Index = index;
        Size = size;
    }

    /// <summary>
    /// 从 2 个 uint32 字解析 IpcPtrBuffDesc
    /// </summary>
    public static IpcPtrBuffDesc FromWords(uint word0, uint word1)
    {
        ulong position = word1;
        position |= ((ulong)word0 << 20) & 0x0f_0000_0000ul;
        position |= ((ulong)word0 << 30) & 0x07_0000_0000_0ul;

        uint index = (word0 >> 0) & 0x03Fu;
        index |= (word0 >> 3) & 0x1C0u;

        ulong size = (ushort)(word0 >> 16);

        return new IpcPtrBuffDesc(position, index, size);
    }

    /// <summary>
    /// 将描述符编码回 2 个 uint32 字
    /// </summary>
    public (uint word0, uint word1) ToWords()
    {
        uint word0 = 0;
        word0 |= (uint)((Address & 0x0f_0000_0000ul) >> 20);
        word0 |= (uint)((Address & 0x07_0000_0000_0ul) >> 30);
        word0 |= (Index & 0x03Fu) << 0;
        word0 |= (Index & 0x1C0u) << 3;
        word0 |= (uint)((ushort)Size) << 16;

        uint word1 = (uint)Address;

        return (word0, word1);
    }

    /// <summary>从组件创建描述符</summary>
    public static IpcPtrBuffDesc Create(ulong address, ulong size, int index)
        => new(address, (uint)(index & 0x1FF), size);

    /// <summary>返回修改 Size 后的新实例</summary>
    public IpcPtrBuffDesc WithSize(ulong newSize)
        => new(Address, Index, newSize);

    public override string ToString() => $"Ptr Addr=0x{Address:X16} Index={Index} Size=0x{Size:X}";
}

/// <summary>
/// IPC 接收列表缓冲区描述符 (C 类型，8 字节 = packed ulong)
/// 遵循 Horizon OS IPC 二进制协议（与 Ryujinx IpcRecvListBuffDesc 一致）
/// 
/// 布局 (8 字节 = 1 × uint64):
///   bits [0:47]   — Position (48 位地址)
///   bits [48:63]  — Size (16 位, 最大 0xFFFF)
/// </summary>
public readonly struct IpcRecvListBuffDesc
{
    /// <summary>原始打包值</summary>
    public ulong Raw { get; }

    public IpcRecvListBuffDesc(ulong raw) => Raw = raw;

    public IpcRecvListBuffDesc(ulong address, ulong size)
    {
        Raw = (address & 0xFFFFFFFFFFFFul) | ((size & 0xFFFFul) << 48);
    }

    /// <summary>缓冲区地址 (Position, 48 位)</summary>
    public ulong Address => Raw & 0xFFFFFFFFFFFFul;

    /// <summary>缓冲区大小 (16 位, 最大 0xFFFF)</summary>
    public ulong Size => (ushort)(Raw >> 48);

    /// <summary>从组件创建描述符</summary>
    public static IpcRecvListBuffDesc Create(ulong address, ulong size)
        => new(address, size);

    public override string ToString() => $"RecvList Addr=0x{Address:X12} Size=0x{Size:X}";
}

/// <summary>IPC 请求</summary>
public sealed class IpcRequest
{
    /// <summary>消息头</summary>
    public IpcMessageHeader Header { get; init; }

    /// <summary>命令 ID (CMIF: 从数据载荷前 4 字节提取; TIPC: 从 header 提取)</summary>
    public uint CommandId { get; init; }

    /// <summary>客户端进程 ID</summary>
    public ulong ClientPid { get; init; }

    /// <summary>请求数据载荷（inline data，不含缓冲区描述符指向的数据）</summary>
    public byte[] Data { get; init; } = [];

    /// <summary>已拷贝句柄</summary>
    public int[] CopyHandles { get; init; } = [];

    /// <summary>已移动句柄</summary>
    public int[] MoveHandles { get; init; } = [];

    // ──────────────────── 缓冲区描述符 ────────────────────

    /// <summary>A 类型缓冲区描述符（发送: 客户端→服务端）</summary>
    public IpcBuffDesc[] SendBuffers { get; init; } = [];

    /// <summary>B 类型缓冲区描述符（接收: 服务端→客户端）</summary>
    public IpcBuffDesc[] ReceiveBuffers { get; init; } = [];

    /// <summary>W 类型缓冲区描述符（交换: 双向）</summary>
    public IpcBuffDesc[] ExchangeBuffers { get; init; } = [];

    /// <summary>X 类型指针缓冲区描述符（服务端内联数据）</summary>
    public IpcPtrBuffDesc[] PointerBuffers { get; init; } = [];

    /// <summary>C 类型接收列表缓冲区描述符</summary>
    public IpcRecvListBuffDesc[] RecvListBuffers { get; init; } = [];

    // ──────────────────── 便捷访问方法 ────────────────────

    /// <summary>按索引获取发送缓冲区描述符</summary>
    public IpcBuffDesc? GetSendBuffer(int index)
        => index >= 0 && index < SendBuffers.Length ? SendBuffers[index] : null;

    /// <summary>按索引获取接收缓冲区描述符</summary>
    public IpcBuffDesc? GetReceiveBuffer(int index)
        => index >= 0 && index < ReceiveBuffers.Length ? ReceiveBuffers[index] : null;

    /// <summary>按索引获取交换缓冲区描述符</summary>
    public IpcBuffDesc? GetExchangeBuffer(int index)
        => index >= 0 && index < ExchangeBuffers.Length ? ExchangeBuffers[index] : null;

    /// <summary>按索引获取指针缓冲区描述符</summary>
    public IpcPtrBuffDesc? GetPointerBuffer(int index)
        => index >= 0 && index < PointerBuffers.Length ? PointerBuffers[index] : null;

    // ──────────────────── 缓冲区数据读写便捷方法 ────────────────────

    /// <summary>
    /// 从指定类型的缓冲区描述符读取数据到字节数组
    /// </summary>
    public byte[] ReadBufferData(VirtualMemoryManager memory, IpcBufferType type, int index = 0)
    {
        var desc = type switch
        {
            IpcBufferType.Send => index < SendBuffers.Length ? (IpcBuffDesc?)SendBuffers[index] : null,
            IpcBufferType.Receive => index < ReceiveBuffers.Length ? (IpcBuffDesc?)ReceiveBuffers[index] : null,
            IpcBufferType.Exchange => index < ExchangeBuffers.Length ? (IpcBuffDesc?)ExchangeBuffers[index] : null,
            _ => null,
        };

        if (desc == null || desc.Value.Size == 0) return [];

        var data = new byte[desc.Value.Size];
        memory.Read(desc.Value.Address, data);
        return data;
    }

    /// <summary>从 X (指针) 缓冲区描述符读取数据</summary>
    public byte[] ReadPointerBufferData(VirtualMemoryManager memory, int index = 0)
    {
        if (index < 0 || index >= PointerBuffers.Length) return [];
        var desc = PointerBuffers[index];
        if (desc.Size == 0) return [];

        var data = new byte[desc.Size];
        memory.Read(desc.Address, data);
        return data;
    }

    /// <summary>向指定类型的缓冲区描述符写入数据</summary>
    public int WriteBufferData(VirtualMemoryManager memory, IpcBufferType type, byte[] data, int index = 0)
    {
        var desc = type switch
        {
            IpcBufferType.Send => index < SendBuffers.Length ? (IpcBuffDesc?)SendBuffers[index] : null,
            IpcBufferType.Receive => index < ReceiveBuffers.Length ? (IpcBuffDesc?)ReceiveBuffers[index] : null,
            IpcBufferType.Exchange => index < ExchangeBuffers.Length ? (IpcBuffDesc?)ExchangeBuffers[index] : null,
            _ => null,
        };

        if (desc == null) return 0;

        int writeLen = (int)Math.Min((ulong)data.Length, desc.Value.Size);
        if (writeLen > 0) memory.Write(desc.Value.Address, data.AsSpan(0, writeLen));
        return writeLen;
    }

    /// <summary>向 X (指针) 缓冲区描述符写入数据</summary>
    public int WritePointerBufferData(VirtualMemoryManager memory, byte[] data, int index = 0)
    {
        if (index < 0 || index >= PointerBuffers.Length) return 0;
        var desc = PointerBuffers[index];

        int writeLen = (int)Math.Min((ulong)data.Length, desc.Size);
        if (writeLen > 0) memory.Write(desc.Address, data.AsSpan(0, writeLen));
        return writeLen;
    }
}

/// <summary>IPC 响应</summary>
public sealed class IpcResponse
{
    /// <summary>结果码</summary>
    public ResultCode ResultCode { get; set; } = ResultCode.Success;

    /// <summary>响应数据（inline data）</summary>
    public List<byte> Data { get; } = new();

    /// <summary>已拷贝句柄</summary>
    public List<int> CopyHandles { get; } = new();
}
