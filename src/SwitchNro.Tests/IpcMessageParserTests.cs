using System;
using System.Buffers.Binary;
using System.Text;
using SwitchNro.HLE.Ipc;
using SwitchNro.Memory;
using Xunit;

namespace SwitchNro.Tests;

public class IpcMessageParserTests : IDisposable
{
    private readonly VirtualMemoryManager _memory;
    private const ulong TestBufferAddr = 0x3000_0000;

    public IpcMessageParserTests()
    {
        _memory = new VirtualMemoryManager();
        _memory.MapZero(TestBufferAddr, 0x1000, MemoryPermissions.ReadWrite);
    }

    public void Dispose() => _memory.Dispose();

    // ──────────────────── CMIF 请求解析测试 ────────────────────

    [Fact]
    public void ParseRequest_CmifRequest_ExtractsCommandType()
    {
        WriteCmifHeader(word0: (uint)IpcCommandType.Request & 0xFFFF, word1: 0);
        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(IpcCommandType.Request, request.Header.CommandType);
    }

    [Fact]
    public void ParseRequest_CmifRequest_ExtractsCommandId()
    {
        // Header: type=5, dataSizeWords=1
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = 1u; // dataSizeWords=1
        WriteCmifHeader(word0, word1);

        // After 2-word header (8 bytes), parser applies 16-byte alignment: pad=8
        // RawData starts at offset 0x10
        WriteUInt32(TestBufferAddr + 0x10, 1u); // commandId=1

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(1u, request.CommandId);
    }

    [Fact]
    public void ParseRequest_CmifRequest_WithHandleDesc()
    {
        // Header: type=5, HndDescEnable, dataSizeWords=1
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = 1u | 0x80000000u; // dataSizeWords=1, HndDescEnable
        WriteCmifHeader(word0, word1);

        // Handle descriptor at offset 0x08: HasPId=0, CopyCount=2
        uint hndWord = (2u << 1); // copy=2
        WriteUInt32(TestBufferAddr + 0x08, hndWord);
        WriteUInt32(TestBufferAddr + 0x0C, 0xD000u); // copy0
        WriteUInt32(TestBufferAddr + 0x10, 0xD001u); // copy1

        // After handle desc: offset = 0x08 + 4 + 2*4 = 0x14
        // 16-byte alignment: pad = (16 - (0x14 & 0xF)) = 12
        // RawData starts at 0x14 + 12 = 0x20
        WriteUInt32(TestBufferAddr + 0x20, 0u); // commandId

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(2, request.CopyHandles.Length);
        Assert.Equal(0xD000, request.CopyHandles[0]);
        Assert.Equal(0xD001, request.CopyHandles[1]);
    }

    [Fact]
    public void ParseRequest_CmifRequest_WithPidDescriptor()
    {
        // Header: type=5, HndDescEnable
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = 1u | 0x80000000u;
        WriteCmifHeader(word0, word1);

        // Handle descriptor: HasPId=1, CopyCount=0
        uint hndWord = 1; // HasPId
        WriteUInt32(TestBufferAddr + 0x08, hndWord);
        WriteUInt64(TestBufferAddr + 0x0C, 0x42UL); // PID

        // After handle desc: offset = 0x08 + 4 + 8(PID) = 0x14
        // 16-byte alignment: pad = 12
        // RawData starts at 0x20
        WriteUInt32(TestBufferAddr + 0x20, 0u);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(0x42UL, request.ClientPid);
    }

    [Fact]
    public void ParseRequest_CmifClose_ExtractsCorrectType()
    {
        WriteCmifHeader(word0: (uint)IpcCommandType.Close & 0xFFFF, word1: 0);
        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(IpcCommandType.Close, request.Header.CommandType);
    }

    // ──────────────────── TIPC 请求解析测试 ────────────────────

    [Fact]
    public void ParseRequest_TipcRequest_ExtractsCommandId()
    {
        // TIPC: Type[0:15] | CommandId[16:31] in Word0
        uint word0 = ((uint)IpcCommandType.TipcRequest & 0xFFFF) | (1u << 16);
        WriteUInt32(TestBufferAddr, word0);

        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(IpcCommandType.TipcRequest, request.Header.CommandType);
        Assert.Equal(1u, request.CommandId);
    }

    // ──────────────────── CMIF 响应写入测试 ────────────────────

    [Fact]
    public void WriteResponse_CmifResponse_WritesHeader()
    {
        var response = new IpcResponse();
        IpcMessageParser.WriteResponse(TestBufferAddr, _memory, response, IpcCommandType.Request);

        uint word0 = _memory.Read<uint>(TestBufferAddr);
        var responseType = (IpcCommandType)(word0 & 0xFFFF);
        Assert.Equal(IpcCommandType.Request, responseType);
    }

    [Fact]
    public void WriteResponse_CmifResponse_WithCopyHandles()
    {
        var response = new IpcResponse();
        response.CopyHandles.Add(0xD000);
        response.CopyHandles.Add(0xD001);

        IpcMessageParser.WriteResponse(TestBufferAddr, _memory, response, IpcCommandType.Request);

        // Verify HndDescEnable in Word1
        uint word1 = _memory.Read<uint>(TestBufferAddr + 4);
        Assert.True((word1 & 0x80000000u) != 0, "HndDescEnable should be set");

        // Handle descriptor at offset 0x08: HasPId=0, CopyCount=2
        uint hndWord = _memory.Read<uint>(TestBufferAddr + 0x08);
        int copyCount = (int)((hndWord >> 1) & 0xF);
        Assert.Equal(2, copyCount);

        // Copy handles at offset 0x0C and 0x10
        Assert.Equal(0xD000u, _memory.Read<uint>(TestBufferAddr + 0x0C));
        Assert.Equal(0xD001u, _memory.Read<uint>(TestBufferAddr + 0x10));
    }

    [Fact]
    public void WriteResponse_CmifResponse_WithData()
    {
        var response = new IpcResponse();
        response.Data.AddRange(BitConverter.GetBytes(0x12345678u));

        IpcMessageParser.WriteResponse(TestBufferAddr, _memory, response, IpcCommandType.Request);

        // Data payload: result code (4 bytes) + response.Data (4 bytes) = 8 bytes = 2 words
        uint word1 = _memory.Read<uint>(TestBufferAddr + 4);
        int dataSizeWords = (int)(word1 & 0x3FF);
        Assert.Equal(2, dataSizeWords);
    }

    [Fact]
    public void WriteResponse_CmifResponse_ResultCodeWritten()
    {
        var response = new IpcResponse();
        response.ResultCode = SwitchNro.Common.ResultCode.Success;

        IpcMessageParser.WriteResponse(TestBufferAddr, _memory, response, IpcCommandType.Request);

        // Result code is in the data payload, after handle descriptor (if any)
        // With 0 copy handles and no HndDescEnable, data starts at offset 0x08
        // Wait — the response writer always puts data after header + handles
        // For 0 handles: offset = CmifHeaderSize(8), data at 0x08
        uint resultCodeValue = _memory.Read<uint>(TestBufferAddr + 0x08);
        Assert.Equal(0u, resultCodeValue); // Success = 0
    }

    // ──────────────────── 往返测试 ────────────────────

    [Fact]
    public void RoundTrip_CmifRequestResponse_PreservesData()
    {
        // 1. Write a CMIF request with handle descriptor
        uint word0 = (uint)IpcCommandType.Request & 0xFFFF;
        uint word1 = 2u | 0x80000000u; // dataSizeWords=2, HndDescEnable
        WriteCmifHeader(word0, word1);

        // Handle descriptor: HasPId=0, CopyCount=1
        uint hndWord = 1u << 1; // copy=1
        WriteUInt32(TestBufferAddr + 0x08, hndWord);
        WriteUInt32(TestBufferAddr + 0x0C, 0xD000u); // copy handle

        // After handle desc: offset = 0x08 + 4 + 1*4 = 0x10
        // 16-byte alignment: pad = 0 (0x10 is already 16-byte aligned)
        // RawData starts at 0x10
        WriteUInt32(TestBufferAddr + 0x10, 42u); // commandId
        WriteUInt32(TestBufferAddr + 0x14, 0xABCDu); // extra data

        // 2. Parse request
        var request = IpcMessageParser.ParseRequest(TestBufferAddr, _memory);
        Assert.Equal(IpcCommandType.Request, request.Header.CommandType);
        Assert.Equal(42u, request.CommandId);
        Assert.Single(request.CopyHandles);
        Assert.Equal(0xD000, request.CopyHandles[0]);

        // 3. Build response and write back
        var response = new IpcResponse();
        response.Data.AddRange(BitConverter.GetBytes(0x1234u));

        IpcMessageParser.WriteResponse(TestBufferAddr, _memory, response, request.Header.CommandType);

        // 4. Verify response header
        uint respWord0 = _memory.Read<uint>(TestBufferAddr);
        Assert.Equal((uint)IpcCommandType.Request, respWord0 & 0xFFFF);
    }

    // ──────────────────── 辅助方法测试 ────────────────────

    [Fact]
    public void ReadServiceName_ExtractsNameFromData()
    {
        // sm:GetService request data: 4 bytes padding + 8 bytes "sm:\0\0\0\0\0"
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0); // padding
        var nameBytes = Encoding.ASCII.GetBytes("sm:");
        Array.Copy(nameBytes, 0, data, 4, nameBytes.Length);

        var name = IpcMessageParser.ReadServiceName(data);
        Assert.Equal("sm:", name);
    }

    [Fact]
    public void ReadServiceName_ShortData_ReturnsName()
    {
        var data = new byte[8];
        var nameBytes = Encoding.ASCII.GetBytes("fs:");
        Array.Copy(nameBytes, 0, data, 0, nameBytes.Length);

        var name = IpcMessageParser.ReadServiceName(data);
        Assert.Equal("fs:", name);
    }

    [Fact]
    public void ReadServiceName_EmptyData_ReturnsEmpty()
    {
        var name = IpcMessageParser.ReadServiceName([]);
        Assert.Equal("", name);
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
