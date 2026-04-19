using System;
using System.Buffers.Binary;
using SwitchNro.HLE.Ipc;
using SwitchNro.Memory;
using Xunit;

namespace SwitchNro.Tests;

/// <summary>
/// IPC 缓冲区描述符 (A/B/W/X/C) 解析与数据读写测试
/// 基于 Horizon OS 真实 IPC 协议格式（与 Ryujinx 一致）
/// </summary>
public class IpcBufferDescriptorTests : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private const ulong TestBufferAddr = 0x3000_0000; // IPC 缓冲区区域
    private const ulong GuestDataAddr = 0x4000_0000;   // Guest 数据区域（描述符指向）

    public IpcBufferDescriptorTests()
    {
        _memory = new VirtualMemoryManager();
        _memory.MapZero(TestBufferAddr, 0x1000, MemoryPermissions.ReadWrite);
        _memory.MapZero(GuestDataAddr, 0x10000, MemoryPermissions.ReadWrite);
    }

    public void Dispose() => _memory.Dispose();

    // ──────────────────── IpcBuffDesc 结构体测试 (12 字节, 3 × uint32) ────────────────────

    [Fact]
    public void IpcBuffDesc_FromWords_ExtractsFields()
    {
        // 构造 3 个 word: Size=0x100, Position=0x4000_0000, Flags=1
        uint word0 = 0x100;    // Size low 32
        uint word1 = 0x40000000u; // Position low 32
        uint word2 = 1;        // Flags=1, plus address/size high bits (0 here)

        var desc = IpcBuffDesc.FromWords(word0, word1, word2);
        Assert.Equal(0x4000_0000UL, desc.Address);
        Assert.Equal(0x100UL, desc.Size);
        Assert.Equal(1, desc.Flags);
    }

    [Fact]
    public void IpcBuffDesc_ToWords_RoundTrip()
    {
        var desc = new IpcBuffDesc(0x4000_0000UL, 0x2000UL, 2);
        var (w0, w1, w2) = desc.ToWords();
        var desc2 = IpcBuffDesc.FromWords(w0, w1, w2);

        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Size, desc2.Size);
        Assert.Equal(desc.Flags, desc2.Flags);
    }

    [Fact]
    public void IpcBuffDesc_Create_SetsFields()
    {
        var desc = IpcBuffDesc.Create(address: 0x4000_0000, size: 0x2000, flags: 0x3);
        Assert.Equal(0x4000_0000UL, desc.Address);
        Assert.Equal(0x2000UL, desc.Size);
        Assert.Equal(0x3, desc.Flags);
    }

    [Fact]
    public void IpcBuffDesc_HighAddress_RoundTrip()
    {
        // Address that uses bits above 32-bit
        var desc = new IpcBuffDesc(0x1_4000_0000UL, 0x100UL, 0);
        var (w0, w1, w2) = desc.ToWords();
        var desc2 = IpcBuffDesc.FromWords(w0, w1, w2);

        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Size, desc2.Size);
        Assert.Equal(desc.Flags, desc2.Flags);
    }

    [Fact]
    public void IpcBuffDesc_LargeSize_RoundTrip()
    {
        // Size that uses bits above 32-bit
        var desc = new IpcBuffDesc(0x4000_0000UL, 0x1_0000_1000UL, 1);
        var (w0, w1, w2) = desc.ToWords();
        var desc2 = IpcBuffDesc.FromWords(w0, w1, w2);

        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Size, desc2.Size);
        Assert.Equal(desc.Flags, desc2.Flags);
    }

    [Fact]
    public void IpcBuffDesc_ToString_ContainsFields()
    {
        var desc = IpcBuffDesc.Create(0x4000_0000, 0x100, 0x1);
        var s = desc.ToString();
        Assert.Contains("40000000", s);
        Assert.Contains("100", s);
    }

    // ──────────────────── IpcPtrBuffDesc 结构体测试 (8 字节, 2 × uint32) ────────────────────

    [Fact]
    public void IpcPtrBuffDesc_FromWords_ExtractsFields()
    {
        // Word0: Index=0, Size=0x80 at bits[16:31], Position high bits
        // Word1: Position low 32 bits
        uint word0 = (0x80u << 16) | 0u; // Size=0x80, Index=0
        uint word1 = 0x40003000u; // Position low 32

        var desc = IpcPtrBuffDesc.FromWords(word0, word1);
        Assert.Equal(0x40003000UL, desc.Address);
        Assert.Equal(0x80UL, desc.Size);
    }

    [Fact]
    public void IpcPtrBuffDesc_ToWords_RoundTrip()
    {
        var desc = new IpcPtrBuffDesc(0x4000_3000UL, 0, 0x80UL);
        var (w0, w1) = desc.ToWords();
        var desc2 = IpcPtrBuffDesc.FromWords(w0, w1);

        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Index, desc2.Index);
        Assert.Equal(desc.Size, desc2.Size);
    }

    [Fact]
    public void IpcPtrBuffDesc_Create_SetsFields()
    {
        var desc = IpcPtrBuffDesc.Create(address: 0x5000, size: 0x100, index: 2);
        Assert.Equal(0x5000UL, desc.Address);
        Assert.Equal(0x100UL, desc.Size);
        Assert.Equal(2u, desc.Index);
    }

    [Fact]
    public void IpcPtrBuffDesc_WithSize_ReturnsNewInstance()
    {
        var desc = IpcPtrBuffDesc.Create(address: 0x5000, size: 0x100, index: 0);
        var desc2 = desc.WithSize(0x200);
        Assert.Equal(0x200UL, desc2.Size);
        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Index, desc2.Index);
    }

    [Fact]
    public void IpcPtrBuffDesc_HighAddress_RoundTrip()
    {
        var desc = new IpcPtrBuffDesc(0x1_4000_3000UL, 3, 0x80UL);
        var (w0, w1) = desc.ToWords();
        var desc2 = IpcPtrBuffDesc.FromWords(w0, w1);

        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Index, desc2.Index);
        Assert.Equal(desc.Size, desc2.Size);
    }

    [Fact]
    public void IpcPtrBuffDesc_ToString_ContainsFields()
    {
        var desc = IpcPtrBuffDesc.Create(address: 0x5000, size: 0x80, index: 0);
        var s = desc.ToString();
        Assert.Contains("5000", s);
        Assert.Contains("80", s);
    }

    // ──────────────────── IpcRecvListBuffDesc 结构体测试 (8 字节, packed ulong) ────────────────────

    [Fact]
    public void IpcRecvListBuffDesc_Packed_ExtractsFields()
    {
        // Position[0:47] | Size[48:63]
        ulong raw = (0x4000_1000UL & 0xFFFFFFFFFFFFul) | (0x200UL << 48);
        var desc = new IpcRecvListBuffDesc(raw);

        Assert.Equal(0x4000_1000UL, desc.Address);
        Assert.Equal(0x200UL, desc.Size);
    }

    [Fact]
    public void IpcRecvListBuffDesc_Create_RoundTrip()
    {
        var desc = IpcRecvListBuffDesc.Create(address: 0x6000, size: 0x200);
        Assert.Equal(0x6000UL, desc.Address);
        Assert.Equal(0x200UL, desc.Size);

        var desc2 = new IpcRecvListBuffDesc(desc.Raw);
        Assert.Equal(desc.Address, desc2.Address);
        Assert.Equal(desc.Size, desc2.Size);
    }

    [Fact]
    public void IpcRecvListBuffDesc_ToString_ContainsFields()
    {
        var desc = IpcRecvListBuffDesc.Create(address: 0x6000, size: 0x200);
        var s = desc.ToString();
        Assert.Contains("6000", s);
        Assert.Contains("200", s);
    }

    // ──────────────────── IpcMessageHeader 字段测试 (2-word format) ────────────────────

    [Fact]
    public void IpcMessageHeader_DescriptorCounts_ExtractedCorrectly()
    {
        // Word0: Type[0:15] | X_count[16:19] | A_count[20:23] | B_count[24:27] | W_count[28:31]
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF)  // Type=5
                    | (2u << 16)   // X=2
                    | (1u << 20)   // A=1
                    | (1u << 24)   // B=1
                    | (0u << 28);  // W=0
        uint word1 = 0; // No data, no recv list

        var h = new IpcMessageHeader(word0, word1);

        Assert.Equal(IpcCommandType.Request, h.CommandType);
        Assert.Equal(2, h.PtrBuffCount);
        Assert.Equal(1, h.SendBuffCount);
        Assert.Equal(1, h.RecvBuffCount);
        Assert.Equal(0, h.XchgBuffCount);
        // Backward compat aliases
        Assert.Equal(2, h.XDescriptorCount);
        Assert.Equal(1, h.ADescriptorCount);
        Assert.Equal(1, h.BDescriptorCount);
        Assert.Equal(0, h.WDescriptorCount);
    }

    [Fact]
    public void IpcMessageHeader_Word1_Fields()
    {
        // Word1: RawDataSize[0:9] | RecvListFlags[10:13] | Reserved[14:30] | HndDescEnable[31]
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = (8u & 0x3FF)        // RawDataSize = 8 words = 32 bytes
                    | (2u << 10)          // RecvListFlags = 2 → 1 C descriptor
                    | (1u << 31);         // HndDescEnable = true

        var h = new IpcMessageHeader(word0, word1);

        Assert.Equal(8, h.RawDataSizeWords);
        Assert.Equal(32, h.RawDataSize);
        Assert.Equal(2, h.RecvListFlags);
        Assert.True(h.HndDescEnable);
    }

    [Fact]
    public void IpcMessageHeader_MaxDescriptorCounts()
    {
        // Each 4-bit count field max = 15
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF)
                    | (15u << 16)  // X=15
                    | (15u << 20)  // A=15
                    | (15u << 24)  // B=15
                    | (15u << 28); // W=15
        uint word1 = 0;

        var h = new IpcMessageHeader(word0, word1);
        Assert.Equal(15, h.PtrBuffCount);
        Assert.Equal(15, h.SendBuffCount);
        Assert.Equal(15, h.RecvBuffCount);
        Assert.Equal(15, h.XchgBuffCount);
    }

    // ──────────────────── IpcHandleDesc 测试 ────────────────────

    [Fact]
    public void IpcHandleDesc_Parse_NoPid()
    {
        // word: HasPId[0]=0 | CopyCount[1:4]=2 | MoveCount[5:8]=1
        uint word = 0 | (2u << 1) | (1u << 5);
        var buf = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, word);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), 0xD000u); // copy0
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), 0xD001u); // copy1
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), 0xE000u); // move0

        var (desc, bytesRead) = IpcHandleDesc.Parse(buf, 0);

        Assert.False(desc.HasPId);
        Assert.Equal(2, desc.CopyHandles.Length);
        Assert.Equal(0xD000, desc.CopyHandles[0]);
        Assert.Equal(0xD001, desc.CopyHandles[1]);
        Assert.Single(desc.MoveHandles);
        Assert.Equal(0xE000, desc.MoveHandles[0]);
        Assert.Equal(4 + 2 * 4 + 1 * 4, bytesRead); // 4 + 8 + 4 = 16
    }

    [Fact]
    public void IpcHandleDesc_Parse_WithPid()
    {
        // word: HasPId[0]=1 | CopyCount[1:4]=1 | MoveCount[5:8]=0
        uint word = 1 | (1u << 1);
        var buf = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, word);
        // PID at offset 4 (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(4), 0x42UL);
        // Copy handle at offset 12
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), 0xD000u);

        var (desc, bytesRead) = IpcHandleDesc.Parse(buf, 0);

        Assert.True(desc.HasPId);
        Assert.Equal(0x42UL, desc.PId);
        Assert.Single(desc.CopyHandles);
        Assert.Equal(0xD000, desc.CopyHandles[0]);
        Assert.Empty(desc.MoveHandles);
        Assert.Equal(4 + 8 + 4, bytesRead); // 4 + 8 + 4 = 16
    }

    [Fact]
    public void IpcHandleDesc_Write_RoundTrip()
    {
        var desc = new IpcHandleDesc([0xD000, 0xD001], [0xE000], 0x42UL, true);
        var buf = new byte[64];
        desc.Write(buf, 0);

        var (desc2, bytesRead) = IpcHandleDesc.Parse(buf, 0);

        Assert.Equal(desc.HasPId, desc2.HasPId);
        Assert.Equal(desc.PId, desc2.PId);
        Assert.Equal(desc.CopyHandles.Length, desc2.CopyHandles.Length);
        Assert.Equal(desc.CopyHandles[0], desc2.CopyHandles[0]);
        Assert.Equal(desc.CopyHandles[1], desc2.CopyHandles[1]);
        Assert.Equal(desc.MoveHandles.Length, desc2.MoveHandles.Length);
        Assert.Equal(desc.MoveHandles[0], desc2.MoveHandles[0]);
        Assert.Equal(desc.Size, bytesRead);
    }

    // ──────────────────── CMIF 解析: X 描述符 ────────────────────

    [Fact]
    public void ParseCmifRequest_OneXDescriptor_ParsedCorrectly()
    {
        // Header: type=5, X=1
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 16);
        uint word1 = 1u; // dataSizeWords=1, no hndDesc
        WriteCmifHeader(word0, word1);

        int offset = 0x08; // After 2-word header

        // X descriptor (8 bytes)
        var xDesc = IpcPtrBuffDesc.Create(address: GuestDataAddr + 0x3000, size: 0x80, index: 0);
        var (w0, w1) = xDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, w0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, w1); offset += 4;

        // 16-byte alignment padding + data
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0u); // commandId

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Single(request.PointerBuffers);
        Assert.Equal(GuestDataAddr + 0x3000, request.PointerBuffers[0].Address);
        Assert.Equal(0x80UL, request.PointerBuffers[0].Size);
    }

    // ──────────────────── CMIF 解析: A 描述符 ────────────────────

    [Fact]
    public void ParseCmifRequest_OneADescriptor_ParsedCorrectly()
    {
        // Header: type=5, A=1
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 20);
        uint word1 = 1u; // dataSizeWords=1
        WriteCmifHeader(word0, word1);

        int offset = 0x08;

        // A descriptor (12 bytes)
        var aDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x100, flags: 0x1);
        var (aw0, aw1, aw2) = aDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, aw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw2); offset += 4;

        // 16-byte alignment + data
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 42u); // commandId

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Single(request.SendBuffers);
        Assert.Equal(GuestDataAddr, request.SendBuffers[0].Address);
        Assert.Equal(0x100UL, request.SendBuffers[0].Size);
        Assert.Equal(0x1, request.SendBuffers[0].Flags);
        Assert.Equal(42u, request.CommandId);
    }

    // ──────────────────── CMIF 解析: B 描述符 ────────────────────

    [Fact]
    public void ParseCmifRequest_OneBDescriptor_ParsedCorrectly()
    {
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 24);
        uint word1 = 1u;
        WriteCmifHeader(word0, word1);

        int offset = 0x08;
        var bDesc = IpcBuffDesc.Create(address: GuestDataAddr + 0x1000, size: 0x200, flags: 0x2);
        var (bw0, bw1, bw2) = bDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, bw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, bw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, bw2); offset += 4;

        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0u);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Single(request.ReceiveBuffers);
        Assert.Equal(GuestDataAddr + 0x1000, request.ReceiveBuffers[0].Address);
        Assert.Equal(0x200UL, request.ReceiveBuffers[0].Size);
    }

    // ──────────────────── CMIF 解析: W 描述符 ────────────────────

    [Fact]
    public void ParseCmifRequest_OneWDescriptor_ParsedCorrectly()
    {
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 28);
        uint word1 = 1u;
        WriteCmifHeader(word0, word1);

        int offset = 0x08;
        var wDesc = IpcBuffDesc.Create(address: GuestDataAddr + 0x2000, size: 0x300, flags: 0x3);
        var (ww0, ww1, ww2) = wDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, ww0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, ww1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, ww2); offset += 4;

        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0u);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Single(request.ExchangeBuffers);
        Assert.Equal(GuestDataAddr + 0x2000, request.ExchangeBuffers[0].Address);
        Assert.Equal(0x300UL, request.ExchangeBuffers[0].Size);
        Assert.Equal(0x3, request.ExchangeBuffers[0].Flags);
    }

    // ──────────────────── CMIF 解析: X→A→B→W 混合 ────────────────────

    [Fact]
    public void ParseCmifRequest_MixedXABWDescriptors_AllParsedInCorrectOrder()
    {
        // Header: type=5, X=1, A=1, B=1, W=1
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF)
                    | (1u << 16)  // X=1
                    | (1u << 20)  // A=1
                    | (1u << 24)  // B=1
                    | (1u << 28); // W=1
        uint word1 = 1u; // dataSizeWords=1
        WriteCmifHeader(word0, word1);

        int offset = 0x08;

        // X (8 bytes) — first per Horizon protocol
        var xDesc = IpcPtrBuffDesc.Create(address: GuestDataAddr + 0x3000, size: 0x80, index: 0);
        var (xw0, xw1) = xDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, xw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, xw1); offset += 4;

        // A (12 bytes)
        var aDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x100, flags: 0x1);
        var (aw0, aw1, aw2) = aDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, aw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw2); offset += 4;

        // B (12 bytes)
        var bDesc = IpcBuffDesc.Create(address: GuestDataAddr + 0x1000, size: 0x200, flags: 0x2);
        var (bw0, bw1, bw2) = bDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, bw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, bw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, bw2); offset += 4;

        // W (12 bytes)
        var wDesc = IpcBuffDesc.Create(address: GuestDataAddr + 0x2000, size: 0x300, flags: 0x3);
        var (ww0, ww1, ww2) = wDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, ww0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, ww1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, ww2); offset += 4;

        // 16-byte alignment + data
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 1u); // commandId

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);

        // X
        Assert.Single(request.PointerBuffers);
        Assert.Equal(GuestDataAddr + 0x3000, request.PointerBuffers[0].Address);
        Assert.Equal(0x80UL, request.PointerBuffers[0].Size);

        // A
        Assert.Single(request.SendBuffers);
        Assert.Equal(GuestDataAddr, request.SendBuffers[0].Address);
        Assert.Equal(0x100UL, request.SendBuffers[0].Size);

        // B
        Assert.Single(request.ReceiveBuffers);
        Assert.Equal(GuestDataAddr + 0x1000, request.ReceiveBuffers[0].Address);
        Assert.Equal(0x200UL, request.ReceiveBuffers[0].Size);

        // W
        Assert.Single(request.ExchangeBuffers);
        Assert.Equal(GuestDataAddr + 0x2000, request.ExchangeBuffers[0].Address);
        Assert.Equal(0x300UL, request.ExchangeBuffers[0].Size);

        Assert.Equal(1u, request.CommandId);
    }

    // ──────────────────── CMIF 解析: Handle Descriptor + A ────────────────────

    [Fact]
    public void ParseCmifRequest_HandleDescAndADescriptor_BothParsed()
    {
        // Header: type=5, A=1, HndDescEnable
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 20); // A=1
        uint word1 = 1u | 0x80000000u; // dataSizeWords=1, HndDescEnable
        WriteCmifHeader(word0, word1);

        int offset = 0x08;

        // Handle descriptor: HasPId=0, CopyCount=1, MoveCount=0
        uint hndWord = 0 | (1u << 1); // copy=1
        WriteUInt32(TestBufferAddr + (ulong)offset, hndWord); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0xD000u); offset += 4; // copy handle

        // A descriptor (12 bytes)
        var aDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x100, flags: 0x1);
        var (aw0, aw1, aw2) = aDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, aw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw2); offset += 4;

        // 16-byte alignment + data
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0u);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Single(request.CopyHandles);
        Assert.Equal(0xD000, request.CopyHandles[0]);
        Assert.Single(request.SendBuffers);
        Assert.Equal(GuestDataAddr, request.SendBuffers[0].Address);
    }

    [Fact]
    public void ParseCmifRequest_PIDAndHandlesAndADescriptor_AllParsed()
    {
        // Header: type=5, A=1, HndDescEnable
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 20);
        uint word1 = 1u | 0x80000000u;
        WriteCmifHeader(word0, word1);

        int offset = 0x08;

        // Handle descriptor: HasPId=1, CopyCount=1
        uint hndWord = 1 | (1u << 1); // HasPId + copy=1
        WriteUInt32(TestBufferAddr + (ulong)offset, hndWord); offset += 4;
        WriteUInt64(TestBufferAddr + (ulong)offset, 0x42UL); offset += 8; // PID
        WriteUInt32(TestBufferAddr + (ulong)offset, 0xD001u); offset += 4; // copy handle

        // A descriptor (12 bytes)
        var aDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x100, flags: 0x1);
        var (aw0, aw1, aw2) = aDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, aw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw2); offset += 4;

        // 16-byte alignment + data
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0u);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);

        Assert.Equal(0x42UL, request.ClientPid);
        Assert.Single(request.CopyHandles);
        Assert.Equal(0xD001, request.CopyHandles[0]);
        Assert.Single(request.SendBuffers);
        Assert.Equal(GuestDataAddr, request.SendBuffers[0].Address);
    }

    // ──────────────────── CMIF 解析: C 描述符 (RecvListFlags) ────────────────────

    [Fact]
    public void ParseCmifRequest_CDescriptorsFromRecvListFlags_ParsedCorrectly()
    {
        // Header: type=5, A=1, RecvListFlags=2 → 1 C descriptor
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 20);
        uint word1 = 1u | (2u << 10); // dataSizeWords=1, RecvListFlags=2
        WriteCmifHeader(word0, word1);

        int offset = 0x08;

        // A descriptor (12 bytes)
        var aDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x100, flags: 0x1);
        var (aw0, aw1, aw2) = aDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, aw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw2); offset += 4;

        // 16-byte alignment + data
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 0u); offset += 4;

        // C descriptor (8 bytes, after data)
        var c0 = IpcRecvListBuffDesc.Create(address: GuestDataAddr + 0x1000, size: 0x200);
        WriteUInt64(TestBufferAddr + (ulong)offset, c0.Raw);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);

        Assert.Single(request.SendBuffers);
        Assert.Single(request.RecvListBuffers);
        Assert.Equal(GuestDataAddr + 0x1000, request.RecvListBuffers[0].Address);
        Assert.Equal(0x200UL, request.RecvListBuffers[0].Size);
    }

    [Fact]
    public void ParseCmifRequest_RecvListFlagsZero_NoCDescriptors()
    {
        // RecvListFlags=0 → no C descriptors
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = 1u; // dataSizeWords=1, RecvListFlags=0
        WriteCmifHeader(word0, word1);

        WriteUInt32(TestBufferAddr + 0x08, 1u); // commandId (after alignment)

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Empty(request.RecvListBuffers);
    }

    // ──────────────────── 无描述符时 ────────────────────

    [Fact]
    public void ParseCmifRequest_NoDescriptors_EmptyArrays()
    {
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = 1u; // dataSizeWords=1
        WriteCmifHeader(word0, word1);

        // After 2-word header (8 bytes), parser applies 16-byte alignment: pad=8
        // RawData starts at offset 0x10
        WriteUInt32(TestBufferAddr + 0x10, 1u); // commandId

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Empty(request.SendBuffers);
        Assert.Empty(request.ReceiveBuffers);
        Assert.Empty(request.ExchangeBuffers);
        Assert.Empty(request.PointerBuffers);
        Assert.Empty(request.RecvListBuffers);
        Assert.Equal(1u, request.CommandId);
    }

    // ──────────────────── IpcRequest.ReadBufferData 测试 ────────────────────

    [Fact]
    public void ReadBufferData_SendBuffer_ReadsFromGuestMemory()
    {
        byte[] testData = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                           0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
        _memory.Write(GuestDataAddr, testData);

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            SendBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0x10, 0x1)],
        };

        var data = request.ReadBufferData(_memory, IpcBufferType.Send);
        Assert.Equal(testData, data);
    }

    [Fact]
    public void ReadBufferData_ReceiveBuffer_ReadsFromGuestMemory()
    {
        byte[] testData = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22,
                           0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00];
        _memory.Write(GuestDataAddr + 0x1000, testData);

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ReceiveBuffers = [IpcBuffDesc.Create(GuestDataAddr + 0x1000, 0x10, 0x2)],
        };

        var data = request.ReadBufferData(_memory, IpcBufferType.Receive);
        Assert.Equal(testData, data);
    }

    [Fact]
    public void ReadBufferData_ExchangeBuffer_ReadsFromGuestMemory()
    {
        byte[] testData = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
                           0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0];
        _memory.Write(GuestDataAddr + 0x2000, testData);

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ExchangeBuffers = [IpcBuffDesc.Create(GuestDataAddr + 0x2000, 0x10, 0x3)],
        };

        var data = request.ReadBufferData(_memory, IpcBufferType.Exchange);
        Assert.Equal(testData, data);
    }

    [Fact]
    public void ReadBufferData_InvalidIndex_ReturnsEmptyArray()
    {
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            SendBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0x10, 0x1)],
        };

        var data = request.ReadBufferData(_memory, IpcBufferType.Send, index: 5);
        Assert.Empty(data);
    }

    [Fact]
    public void ReadBufferData_ZeroSize_ReturnsEmptyArray()
    {
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            SendBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0, 0x1)],
        };

        var data = request.ReadBufferData(_memory, IpcBufferType.Send);
        Assert.Empty(data);
    }

    // ──────────────────── IpcRequest.ReadPointerBufferData 测试 ────────────────────

    [Fact]
    public void ReadPointerBufferData_ReadsFromGuestMemory()
    {
        byte[] testData = [0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
                           0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51];
        _memory.Write(GuestDataAddr + 0x3000, testData);

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            PointerBuffers = [IpcPtrBuffDesc.Create(GuestDataAddr + 0x3000, 0x10, 0)],
        };

        var data = request.ReadPointerBufferData(_memory);
        Assert.Equal(testData, data);
    }

    [Fact]
    public void ReadPointerBufferData_InvalidIndex_ReturnsEmptyArray()
    {
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            PointerBuffers = [IpcPtrBuffDesc.Create(GuestDataAddr, 0x10, 0)],
        };

        var data = request.ReadPointerBufferData(_memory, index: 3);
        Assert.Empty(data);
    }

    // ──────────────────── IpcRequest.WriteBufferData 测试 ────────────────────

    [Fact]
    public void WriteBufferData_ReceiveBuffer_WritesToGuestMemory()
    {
        byte[] writeData = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80,
                            0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00];

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ReceiveBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0x10, 0x2)],
        };

        int written = request.WriteBufferData(_memory, IpcBufferType.Receive, writeData);
        Assert.Equal(0x10, written);

        var readBack = new byte[0x10];
        _memory.Read(GuestDataAddr, readBack);
        Assert.Equal(writeData, readBack);
    }

    [Fact]
    public void WriteBufferData_DataLargerThanDescriptor_TruncatesWrite()
    {
        byte[] writeData = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
                            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18]; // 24 bytes

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ReceiveBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0x10, 0x2)], // Size=0x10 only
        };

        int written = request.WriteBufferData(_memory, IpcBufferType.Receive, writeData);
        Assert.Equal(0x10, written);

        var readBack = new byte[0x10];
        _memory.Read(GuestDataAddr, readBack);
        Assert.Equal(writeData[..0x10], readBack);
    }

    [Fact]
    public void WriteBufferData_InvalidIndex_ReturnsZero()
    {
        byte[] writeData = [0x01, 0x02];

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ReceiveBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0x10, 0x2)],
        };

        int written = request.WriteBufferData(_memory, IpcBufferType.Receive, writeData, index: 5);
        Assert.Equal(0, written);
    }

    // ──────────────────── IpcRequest.WritePointerBufferData 测试 ────────────────────

    [Fact]
    public void WritePointerBufferData_WritesToGuestMemory()
    {
        byte[] writeData = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22,
                            0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00];

        // GuestDataAddr + 0x5000 is already mapped by the constructor (0x10000 bytes total)

        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            PointerBuffers = [IpcPtrBuffDesc.Create(GuestDataAddr + 0x5000, 0x10, 0)],
        };

        int written = request.WritePointerBufferData(_memory, writeData);
        Assert.Equal(0x10, written);

        var readBack = new byte[0x10];
        _memory.Read(GuestDataAddr + 0x5000, readBack);
        Assert.Equal(writeData, readBack);
    }

    // ──────────────────── IpcRequest 便捷访问器测试 ────────────────────

    [Fact]
    public void GetSendBuffer_ValidIndex_ReturnsDescriptor()
    {
        var desc = IpcBuffDesc.Create(GuestDataAddr, 0x100, 0x1);
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            SendBuffers = [desc],
        };

        var result = request.GetSendBuffer(0);
        Assert.NotNull(result);
        Assert.Equal(desc.Address, result!.Value.Address);
        Assert.Equal(desc.Size, result.Value.Size);
    }

    [Fact]
    public void GetSendBuffer_InvalidIndex_ReturnsNull()
    {
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            SendBuffers = [IpcBuffDesc.Create(GuestDataAddr, 0x100, 0x1)],
        };

        Assert.Null(request.GetSendBuffer(-1));
        Assert.Null(request.GetSendBuffer(1));
    }

    [Fact]
    public void GetReceiveBuffer_ValidIndex_ReturnsDescriptor()
    {
        var desc = IpcBuffDesc.Create(GuestDataAddr, 0x200, 0x2);
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ReceiveBuffers = [desc],
        };

        var result = request.GetReceiveBuffer(0);
        Assert.NotNull(result);
        Assert.Equal(desc.Address, result!.Value.Address);
        Assert.Equal(desc.Size, result.Value.Size);
    }

    [Fact]
    public void GetExchangeBuffer_ValidIndex_ReturnsDescriptor()
    {
        var desc = IpcBuffDesc.Create(GuestDataAddr, 0x300, 0x3);
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            ExchangeBuffers = [desc],
        };

        var result = request.GetExchangeBuffer(0);
        Assert.NotNull(result);
        Assert.Equal(desc.Address, result!.Value.Address);
        Assert.Equal(desc.Size, result.Value.Size);
    }

    [Fact]
    public void GetPointerBuffer_ValidIndex_ReturnsDescriptor()
    {
        var desc = IpcPtrBuffDesc.Create(GuestDataAddr, 0x80, 0);
        var request = new IpcRequest
        {
            Header = new IpcMessageHeader(0, 0),
            CommandId = 0,
            PointerBuffers = [desc],
        };

        var result = request.GetPointerBuffer(0);
        Assert.NotNull(result);
        Assert.Equal(desc.Address, result!.Value.Address);
        Assert.Equal(desc.Size, result.Value.Size);
    }

    // ──────────────────── 完整 IPC 流程: 描述符 + 数据 ────────────────────

    [Fact]
    public void FullRoundTrip_SendBufferWithData_ReadBufferDataReturnsSameData()
    {
        // 1. 在 guest 内存写入原始数据
        byte[] originalData = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
                               0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0];
        _memory.Write(GuestDataAddr, originalData);

        // 2. 构造 IPC 请求: A=1 描述符指向该数据
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 20);
        uint word1 = 1u;
        WriteCmifHeader(word0, word1);

        int offset = 0x08;
        var aDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x10, flags: 0x1);
        var (aw0, aw1, aw2) = aDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, aw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, aw2); offset += 4;

        // Alignment + commandId
        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 1u);

        // 3. 解析并通过 ReadBufferData 读取
        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        var readData = request.ReadBufferData(_memory, IpcBufferType.Send);

        Assert.Equal(originalData, readData);
    }

    [Fact]
    public void FullRoundTrip_ReceiveBuffer_WriteBufferDataWritesToGuest()
    {
        // 1. 构造 IPC 请求: B=1 描述符指向 guest 区域
        uint word0 = ((uint)IpcCommandType.Request & 0xFFFF) | (1u << 24);
        uint word1 = 1u;
        WriteCmifHeader(word0, word1);

        int offset = 0x08;
        var bDesc = IpcBuffDesc.Create(address: GuestDataAddr, size: 0x200, flags: 0x2);
        var (bw0, bw1, bw2) = bDesc.ToWords();
        WriteUInt32(TestBufferAddr + (ulong)offset, bw0); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, bw1); offset += 4;
        WriteUInt32(TestBufferAddr + (ulong)offset, bw2); offset += 4;

        int pad = (16 - (offset & 0xF)) & 0xF;
        offset += pad;
        WriteUInt32(TestBufferAddr + (ulong)offset, 2u); // commandId

        // 2. 解析并通过 WriteBufferData 写入响应数据
        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        byte[] responseData = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                               0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
        int written = request.WriteBufferData(_memory, IpcBufferType.Receive, responseData);

        Assert.Equal(0x10, written);

        // 3. 验证 guest 内存中的数据
        var readBack = new byte[0x10];
        _memory.Read(GuestDataAddr, readBack);
        Assert.Equal(responseData, readBack);
    }

    // ──────────────────── 辅助方法 ────────────────────

    private void WriteCmifHeader(uint word0, uint word1)
    {
        WriteUInt32(TestBufferAddr, word0);
        WriteUInt32(TestBufferAddr + 4, word1);
    }

    private void WriteUInt32(ulong addr, uint value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        _memory.Write(addr, buf);
    }

    private void WriteUInt64(ulong addr, ulong value)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        _memory.Write(addr, buf);
    }
}
