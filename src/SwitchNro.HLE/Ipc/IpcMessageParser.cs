using System;
using System.Buffers.Binary;
using System.Text;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Memory;

namespace SwitchNro.HLE.Ipc;

/// <summary>
/// IPC 消息解析器
/// 解析/写入 Horizon OS 的 IPC 二进制协议（CMIF 和 TIPC 格式）
/// 从 guest 虚拟内存中读取请求、写入响应
/// 
/// ──────────────────── CMIF 请求布局 ────────────────────
///  偏移 0x00: uint32 Word0 (Type[0:15] | X_count[16:19] | A_count[20:23] | B_count[24:27] | W_count[28:31])
///  偏移 0x04: uint32 Word1 (RawDataSize[0:9] | RecvListFlags[10:13] | Reserved[14:30] | HndDescEnable[31])
///  偏移 0x08: Handle Descriptor (如果 HndDescEnable):
///               uint32 word (HasPId[0] | CopyCount[1:4] | MoveCount[5:8])
///               ulong PId (如果 HasPId)
///               uint32[] CopyHandles
///               uint32[] MoveHandles
///  偏移 ...  : X 指针缓冲区描述符: IpcPtrBuffDesc[] (8 字节/个, 2 × uint32)
///  偏移 ...  : A 发送缓冲区描述符: IpcBuffDesc[] (12 字节/个, 3 × uint32)
///  偏移 ...  : B 接收缓冲区描述符: IpcBuffDesc[] (12 字节/个, 3 × uint32)
///  偏移 ...  : W 交换缓冲区描述符: IpcBuffDesc[] (12 字节/个, 3 × uint32)
///  偏移 ...  : 16 字节对齐填充
///  偏移 ...  : RawData 数据载荷 (第一个 uint 通常是 commandId)
///  偏移 ...  : C 接收列表缓冲区描述符: IpcRecvListBuffDesc[] (8 字节/个)
/// ────────────────────────────────────────────────────────
/// </summary>
public static class IpcMessageParser
{
    /// <summary>IPC 缓冲区最大大小（TLS 区域通常 0x100 字节）</summary>
    private const int IpcBufferSize = 0x100;

    // CMIF 头部固定区域大小 (2 × uint32 = 8 bytes)
    private const int CmifHeaderSize = 0x08;

    // TIPC 头部固定区域大小 (4 × uint32 = 16 bytes)
    private const int TipcHeaderSize = 0x10;

    // IpcBuffDesc 大小 (3 × uint32 = 12 bytes)
    private const int BuffDescSize = 12;

    // IpcPtrBuffDesc 大小 (2 × uint32 = 8 bytes)
    private const int PtrBuffDescSize = 8;

    // IpcRecvListBuffDesc 大小 (1 × uint64 = 8 bytes)
    private const int RecvListBuffDescSize = 8;

    /// <summary>
    /// 快速从 IPC 缓冲区读取命令 ID
    /// </summary>
    public static uint ReadCommandId(ulong bufferAddr, VirtualMemoryManager memory)
    {
        try
        {
            var headerBuf = new byte[8];
            memory.Read(bufferAddr, headerBuf);
            uint word0 = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(0));
            uint word1 = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(4));
            var header = new IpcMessageHeader(word0, word1);

            if (header.CommandType is IpcCommandType.TipcRequest or IpcCommandType.TipcControl)
                return header.TipcCommandId;

            // CMIF: 跳过 Header 和句柄描述符
            int offset = CmifHeaderSize;
            if (header.HndDescEnable)
            {
                var hndWordBuf = new byte[4];
                memory.Read(bufferAddr + (ulong)offset, hndWordBuf);
                uint hndWord = BinaryPrimitives.ReadUInt32LittleEndian(hndWordBuf);
                bool hasPid = (hndWord & 1) != 0;
                int copyCount = (int)((hndWord >> 1) & 0xF);
                int moveCount = (int)((hndWord >> 5) & 0xF);
                offset += 4 + (hasPid ? 8 : 0) + (copyCount + moveCount) * 4;
            }

            // 跳过 X 描述符 (每个 8 字节)
            offset += header.PtrBuffCount * 8;
            // 跳过 A, B, W 描述符 (每个 12 字节)
            offset += (header.SendBuffCount + header.RecvBuffCount + header.XchgBuffCount) * 12;
            // 16 字节对齐
            offset = (offset + 15) & ~15;

            var cmdIdBuf = new byte[4];
            memory.Read(bufferAddr + (ulong)offset, cmdIdBuf);
            return BinaryPrimitives.ReadUInt32LittleEndian(cmdIdBuf);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 从 guest 内存解析 IPC 请求
    /// </summary>
    public static IpcRequest ParseRequest(ulong bufferAddr, VirtualMemoryManager memory)
    {
        // 读取整个 IPC 缓冲区
        var buffer = new byte[IpcBufferSize];
        memory.Read(bufferAddr, buffer);

        // CMIF: Word0 + Word1
        uint word0 = ReadUInt32(buffer, 0);
        uint word1 = ReadUInt32(buffer, 4);
        var header = new IpcMessageHeader(word0, word1);
        var cmdType = header.CommandType;

        Logger.Debug(nameof(IpcMessageParser),
            $"解析 IPC 请求 @ 0x{bufferAddr:X16}: type={cmdType} w0=0x{word0:X8} w1=0x{word1:X8}");

        // TIPC 格式（TipcRequest / TipcClose / TipcControl）
        if (cmdType is IpcCommandType.TipcRequest or IpcCommandType.TipcClose or IpcCommandType.TipcControl)
        {
            return ParseTipcRequest(buffer, header);
        }

        // CMIF 格式（Request / Control / Close / Legacy 等）
        return ParseCmifRequest(buffer, header);
    }

    /// <summary>
    /// 将 IPC 响应写入 guest 内存
    /// </summary>
    public static void WriteResponse(ulong bufferAddr, VirtualMemoryManager memory,
        IpcResponse response, IpcCommandType cmdType)
    {
        // 清空缓冲区
        var buffer = new byte[IpcBufferSize];

        if (cmdType is IpcCommandType.TipcRequest or IpcCommandType.TipcClose or IpcCommandType.TipcControl)
        {
            WriteTipcResponse(buffer, response, cmdType);
        }
        else
        {
            WriteCmifResponse(buffer, response, cmdType);
        }

        memory.Write(bufferAddr, buffer);

        Logger.Debug(nameof(IpcMessageParser),
            $"写入 IPC 响应 @ 0x{bufferAddr:X16}: type={cmdType} data={response.Data.Count}B handles={response.CopyHandles.Count}");
    }

    // ──────────────────── CMIF 请求解析 ────────────────────

    private static IpcRequest ParseCmifRequest(byte[] buffer, IpcMessageHeader header)
    {
        var cmdType = header.CommandType;

        // 从头部提取缓冲区描述符数量
        int xCount = header.PtrBuffCount;
        int aCount = header.SendBuffCount;
        int bCount = header.RecvBuffCount;
        int wCount = header.XchgBuffCount;

        // 数据大小（以字为单位）
        int dataSizeWords = header.RawDataSizeWords;
        int dataSize = dataSizeWords * 4;

        // 当前读取偏移（从头部固定区域之后开始）
        int offset = CmifHeaderSize;

        // ──────── 句柄描述符 ────────
        ulong clientPid = 0;
        int[] copyHandles = [];
        int[] moveHandles = [];

        if (header.HndDescEnable)
        {
            var (handleDesc, bytesRead) = IpcHandleDesc.Parse(buffer, offset);
            offset += bytesRead;

            clientPid = handleDesc.HasPId ? handleDesc.PId : 0;
            copyHandles = handleDesc.CopyHandles;
            moveHandles = handleDesc.MoveHandles;
        }

        // ──────── X 指针缓冲区描述符（第一个，8 字节/个）────────
        int xRead = 0;
        var ptrBuffers = new IpcPtrBuffDesc[xCount];
        for (int i = 0; i < xCount && offset + PtrBuffDescSize <= buffer.Length; i++)
        {
            uint w0 = ReadUInt32(buffer, offset);
            uint w1 = ReadUInt32(buffer, offset + 4);
            ptrBuffers[i] = IpcPtrBuffDesc.FromWords(w0, w1);
            offset += PtrBuffDescSize;
            xRead++;
        }
        // 跳过因缓冲区不足而未读取的 X 描述符空间
        int xSkipped = xCount - xRead;
        if (xSkipped > 0 && offset + xSkipped * PtrBuffDescSize <= buffer.Length)
            offset += xSkipped * PtrBuffDescSize;

        // ──────── A 发送缓冲区描述符（12 字节/个）────────
        int aRead = 0;
        var sendBuffers = new IpcBuffDesc[aCount];
        for (int i = 0; i < aCount && offset + BuffDescSize <= buffer.Length; i++)
        {
            uint w0 = ReadUInt32(buffer, offset);
            uint w1 = ReadUInt32(buffer, offset + 4);
            uint w2 = ReadUInt32(buffer, offset + 8);
            sendBuffers[i] = IpcBuffDesc.FromWords(w0, w1, w2);
            offset += BuffDescSize;
            aRead++;
        }
        if (aCount - aRead > 0 && offset + (aCount - aRead) * BuffDescSize <= buffer.Length)
            offset += (aCount - aRead) * BuffDescSize;

        // ──────── B 接收缓冲区描述符（12 字节/个）────────
        int bRead = 0;
        var recvBuffers = new IpcBuffDesc[bCount];
        for (int i = 0; i < bCount && offset + BuffDescSize <= buffer.Length; i++)
        {
            uint w0 = ReadUInt32(buffer, offset);
            uint w1 = ReadUInt32(buffer, offset + 4);
            uint w2 = ReadUInt32(buffer, offset + 8);
            recvBuffers[i] = IpcBuffDesc.FromWords(w0, w1, w2);
            offset += BuffDescSize;
            bRead++;
        }
        if (bCount - bRead > 0 && offset + (bCount - bRead) * BuffDescSize <= buffer.Length)
            offset += (bCount - bRead) * BuffDescSize;

        // ──────── W 交换缓冲区描述符（12 字节/个）────────
        int wRead = 0;
        var exchangeBuffers = new IpcBuffDesc[wCount];
        for (int i = 0; i < wCount && offset + BuffDescSize <= buffer.Length; i++)
        {
            uint w0 = ReadUInt32(buffer, offset);
            uint w1 = ReadUInt32(buffer, offset + 4);
            uint w2 = ReadUInt32(buffer, offset + 8);
            exchangeBuffers[i] = IpcBuffDesc.FromWords(w0, w1, w2);
            offset += BuffDescSize;
            wRead++;
        }
        if (wCount - wRead > 0 && offset + (wCount - wRead) * BuffDescSize <= buffer.Length)
            offset += (wCount - wRead) * BuffDescSize;

        // ──────── 16 字节对齐填充 ────────
        int pad = GetPadSize16(offset);
        offset += pad;

        // 保存 C 描述符的起始位置（在 rawData 之后）
        int recvListPos = offset + dataSize;

        // ──────── RawData 数据载荷 ────────
        byte[] rawData = [];
        if (dataSize > 0 && offset + dataSize <= buffer.Length)
        {
            rawData = new byte[dataSize];
            Array.Copy(buffer, offset, rawData, 0, dataSize);
        }
        else if (dataSize > 0)
        {
            int availSize = Math.Max(0, buffer.Length - offset);
            if (availSize > 0)
            {
                rawData = new byte[availSize];
                Array.Copy(buffer, offset, rawData, 0, availSize);
            }
        }

        // ──────── C 接收列表缓冲区描述符 ────────
        int recvListFlags = header.RecvListFlags;
        int cCount = ComputeRecvListCount(recvListFlags);

        var recvListBuffers = new IpcRecvListBuffDesc[cCount];
        int cOffset = Math.Min(recvListPos, buffer.Length);
        for (int i = 0; i < cCount && cOffset + RecvListBuffDescSize <= buffer.Length; i++)
        {
            ulong raw = ReadUInt64(buffer, cOffset);
            recvListBuffers[i] = new IpcRecvListBuffDesc(raw);
            cOffset += RecvListBuffDescSize;
        }

        // 从数据载荷提取 commandId（CMIF Request 的数据前 4 字节为 commandId）
        uint commandId = 0;
        byte[] data = [];
        if (rawData.Length >= 4)
        {
            commandId = ReadUInt32(rawData, 0);
            data = new byte[rawData.Length - 4];
            if (data.Length > 0)
                Array.Copy(rawData, 4, data, 0, data.Length);
        }
        else if (rawData.Length > 0)
        {
            data = rawData;
        }

        Logger.Debug(nameof(IpcMessageParser),
            $"CMIF 请求: cmdType={cmdType} cmdId={commandId} pid={clientPid} " +
            $"copyH={copyHandles.Length} moveH={moveHandles.Length} " +
            $"X={xCount} A={aCount} B={bCount} W={wCount} C={recvListBuffers.Length} " +
            $"dataSize={dataSize}");

        return new IpcRequest
        {
            Header = header,
            CommandId = commandId,
            ClientPid = clientPid,
            Data = data,
            CopyHandles = copyHandles,
            MoveHandles = moveHandles,
            SendBuffers = sendBuffers,
            ReceiveBuffers = recvBuffers,
            ExchangeBuffers = exchangeBuffers,
            PointerBuffers = ptrBuffers,
            RecvListBuffers = recvListBuffers,
        };
    }

    // ──────────────────── TIPC 请求解析 ────────────────────

    private static IpcRequest ParseTipcRequest(byte[] buffer, IpcMessageHeader header)
    {
        // TIPC 格式: header(4) + padding(4) + padding(4) + padding(4) + data...
        // Type 在 Word0[0:15], CommandId 在 Word0[16:31]
        uint commandId = header.TipcCommandId;

        int offset = TipcHeaderSize;

        // 数据载荷
        int remaining = Math.Min(buffer.Length - offset, IpcBufferSize - offset);
        byte[] data = [];
        if (remaining > 0)
        {
            data = new byte[remaining];
            Array.Copy(buffer, offset, data, 0, remaining);
        }

        // TIPC 请求中的 handles 简化处理
        var copyHandles = Array.Empty<int>();
        var moveHandles = Array.Empty<int>();

        Logger.Debug(nameof(IpcMessageParser),
            $"TIPC 请求: cmdType={header.CommandType} cmdId={commandId} dataSize={data.Length}");

        return new IpcRequest
        {
            Header = header,
            CommandId = commandId,
            ClientPid = 0,
            Data = data,
            CopyHandles = copyHandles,
            MoveHandles = moveHandles,
        };
    }

    // ──────────────────── CMIF 响应写入 ────────────────────

    private static void WriteCmifResponse(byte[] buffer, IpcResponse response, IpcCommandType cmdType)
    {
        int copyHandleCount = response.CopyHandles.Count;
        // 数据载荷包含: result code (4 bytes) + response.Data，向上对齐到 4 字节
        int totalDataSize = 4 + response.Data.Count;
        int dataSizeWords = (totalDataSize + 3) / 4;

        // 构建 CMIF 响应头
        // Word0: Type[0:15] (与 CMIF 请求相同, 含完整 command type)
        // Word1: RawDataSize[0:9] | HndDescEnable[31]
        uint word0 = (uint)cmdType & 0xFFFF;
        uint word1 = (uint)(dataSizeWords & 0x3FF);
        if (copyHandleCount > 0)
            word1 |= 0x80000000u; // HndDescEnable

        WriteUInt32(buffer, 0, word0);
        WriteUInt32(buffer, 4, word1);

        int offset = CmifHeaderSize;

        // 句柄描述符（如果需要）
        if (copyHandleCount > 0)
        {
            var handleDesc = new IpcHandleDesc(response.CopyHandles.ToArray(), [], 0, false);
            handleDesc.Write(buffer, offset);
            offset += handleDesc.Size;
        }

        // RawData（result code 作为数据载荷的第一个 uint32）
        // 注意: 响应写入不使用 16 字节对齐填充 — Horizon 响应格式中 rawData 紧跟句柄描述符之后
        WriteUInt32(buffer, offset, (uint)response.ResultCode.Value);
        offset += 4;

        if (response.Data.Count > 0)
        {
            int copyLen = Math.Min(response.Data.Count, buffer.Length - offset);
            if (copyLen > 0)
            {
                response.Data.CopyTo(0, buffer, offset, copyLen);
                offset += copyLen;
            }
        }
    }

    // ──────────────────── TIPC 响应写入 ────────────────────

    private static void WriteTipcResponse(byte[] buffer, IpcResponse response, IpcCommandType cmdType)
    {
        int dataSizeWords = (response.Data.Count + 7) / 4; // 含 result code

        // TIPC 响应头: Type[0:15] | DataSizeWords[16:31]
        uint word0 = ((uint)cmdType & 0xFFFF) | ((uint)(dataSizeWords & 0xFFFF) << 16);
        WriteUInt32(buffer, 0, word0);

        // 结果码
        WriteUInt32(buffer, 4, (uint)response.ResultCode.Value);
        // padding
        WriteUInt32(buffer, 8, 0);
        WriteUInt32(buffer, 12, 0);

        int offset = TipcHeaderSize;

        // Copy handles (TIPC 通常无句柄)
        foreach (var handle in response.CopyHandles)
        {
            if (offset + 4 > buffer.Length) break;
            WriteUInt32(buffer, offset, (uint)handle);
            offset += 4;
        }

        // 数据载荷
        if (response.Data.Count > 0)
        {
            int copyLen = Math.Min(response.Data.Count, buffer.Length - offset);
            if (copyLen > 0)
            {
                response.Data.CopyTo(0, buffer, offset, copyLen);
            }
        }
    }

    // ──────────────────── 辅助方法 ────────────────────

    /// <summary>
    /// 计算 C 接收列表描述符数量
    /// recvListFlags == 0: 无 C 描述符
    /// recvListFlags == 1: 无 C 描述符
    /// recvListFlags == 2: 1 个 C 描述符
    /// recvListFlags > 2: (recvListFlags - 2) 个 C 描述符
    /// </summary>
    private static int ComputeRecvListCount(int recvListFlags)
    {
        int count = recvListFlags - 2;
        if (count == 0) count = 1;
        else if (count < 0) count = 0;
        return count;
    }

    /// <summary>
    /// 计算 16 字节对齐填充大小
    /// </summary>
    private static int GetPadSize16(int position)
    {
        int mask = position & 0xF;
        return mask != 0 ? 0x10 - mask : 0;
    }

    /// <summary>
    /// 从 IPC 请求数据中读取服务名称
    /// 通常位于 data[0..7]（8 字节 null-terminated ASCII）
    /// </summary>
    public static string ReadServiceName(byte[] data)
    {
        if (data.Length < 8) return "";
        // 跳过前 4 字节的 commandId/padding，读取后续 8 字节
        // sm:GetService 的 data 格式: [4字节 padding] + [8字节 服务名]
        int nameOffset = data.Length >= 12 ? 4 : 0;
        int nameLen = Math.Min(8, data.Length - nameOffset);
        var nameBytes = new byte[nameLen];
        Array.Copy(data, nameOffset, nameBytes, 0, nameLen);
        return Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
    }

    /// <summary>从字节数组指定偏移读取 UInt32（小端序）</summary>
    private static uint ReadUInt32(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));

    /// <summary>向字节数组指定偏移写入 UInt32（小端序）</summary>
    private static void WriteUInt32(byte[] buffer, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    /// <summary>从字节数组指定偏移读取 UInt64（小端序）</summary>
    private static ulong ReadUInt64(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));
}
