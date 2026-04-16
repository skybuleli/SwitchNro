using System;
using System.Text;
using SwitchNro.Common;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using Xunit;

using static SwitchNro.Tests.IpcTestHelper;

namespace SwitchNro.Tests;

/// <summary>
/// 加载器服务单元测试 — LdrShelService, LdrDmntService, LdrPmService
/// </summary>
public class LdrServiceTests
{
    // ──────────────────────────── LdrShelService (ldr:shel) ────────────────────────────

    [Fact]
    public void LdrShelService_PortName_是ldrShel()
    {
        Assert.Equal("ldr:shel", new LdrShelService().PortName);
    }

    [Fact]
    public void LdrShelService_命令表包含0和1()
    {
        var service = new LdrShelService();
        Assert.True(service.CommandTable.ContainsKey(0)); // SetProgramArgument
        Assert.True(service.CommandTable.ContainsKey(1)); // FlushArguments
        Assert.Equal(2, service.CommandTable.Count);
    }

    [Fact]
    public void LdrShelService_未知命令返回错误()
    {
        var (result, _) = InvokeCommand(new LdrShelService(), 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrShelService_SetProgramArgument_空数据返回Ldr错误()
    {
        var (result, _) = InvokeCommand(new LdrShelService(), 0);
        Assert.False(result.IsSuccess);
        Assert.Equal(9, result.Module); // LdrResult module = 9
    }

    [Fact]
    public void LdrShelService_SetProgramArgument_数据不足16字节返回错误()
    {
        var (result, _) = InvokeCommand(new LdrShelService(), 0, new byte[15]);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrShelService_SetProgramArgument_无参数字符串返回成功()
    {
        // u32 size(0) + padding(4) + u64 programId = 16 bytes, 无附加字符串
        var data = new byte[16];
        BitConverter.GetBytes(0U).CopyTo(data, 0);       // argSize = 0
        BitConverter.GetBytes(0x123456789ABCDEFUL).CopyTo(data, 8); // programId

        var (result, response) = InvokeCommand(new LdrShelService(), 0, data);
        Assert.True(result.IsSuccess);
        Assert.Empty(response.Data);
    }

    [Fact]
    public void LdrShelService_SetProgramArgument_带参数字符串返回成功()
    {
        var args = "--test-flag\0"u8.ToArray();
        var data = new byte[16 + args.Length];
        BitConverter.GetBytes((uint)args.Length).CopyTo(data, 0);   // argSize
        BitConverter.GetBytes(0xDEADBEEFUL).CopyTo(data, 8);        // programId
        args.CopyTo(data, 16);

        var service = new LdrShelService();
        var (result, _) = InvokeCommand(service, 0, data);
        Assert.True(result.IsSuccess);

        // 通过 GetProgramArgument 验证存储
        var stored = service.GetProgramArgument(0xDEADBEEFUL);
        Assert.NotNull(stored);
        Assert.Equal("--test-flag", stored);
    }

    [Fact]
    public void LdrShelService_GetProgramArgument_未设置返回null()
    {
        var service = new LdrShelService();
        Assert.Null(service.GetProgramArgument(0xFFFFFFFFUL));
    }

    [Fact]
    public void LdrShelService_FlushArguments_返回成功()
    {
        var (result, response) = InvokeCommand(new LdrShelService(), 1);
        Assert.True(result.IsSuccess);
        Assert.Empty(response.Data);
    }

    [Fact]
    public void LdrShelService_SetProgramArgument_可覆盖旧参数()
    {
        var service = new LdrShelService();
        var programId = 0x1000UL;

        // 第一次设置
        var data1 = BuildSetArgData(programId, "first");
        InvokeCommand(service, 0, data1);
        Assert.Equal("first", service.GetProgramArgument(programId));

        // 第二次覆盖
        var data2 = BuildSetArgData(programId, "second");
        InvokeCommand(service, 0, data2);
        Assert.Equal("second", service.GetProgramArgument(programId));
    }

    // ──────────────────────────── LdrDmntService (ldr:dmnt) ────────────────────────────

    [Fact]
    public void LdrDmntService_PortName_是ldrDmnt()
    {
        Assert.Equal("ldr:dmnt", new LdrDmntService().PortName);
    }

    [Fact]
    public void LdrDmntService_命令表包含0和1和2()
    {
        var service = new LdrDmntService();
        Assert.True(service.CommandTable.ContainsKey(0)); // SetProgramArgument2
        Assert.True(service.CommandTable.ContainsKey(1)); // FlushArguments2
        Assert.True(service.CommandTable.ContainsKey(2)); // GetProcessModuleInfo
        Assert.Equal(3, service.CommandTable.Count);
    }

    [Fact]
    public void LdrDmntService_未知命令返回错误()
    {
        var (result, _) = InvokeCommand(new LdrDmntService(), 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrDmntService_SetProgramArgument2_空数据返回Ldr错误()
    {
        var (result, _) = InvokeCommand(new LdrDmntService(), 0);
        Assert.False(result.IsSuccess);
        Assert.Equal(9, result.Module);
    }

    [Fact]
    public void LdrDmntService_SetProgramArgument2_数据不足16字节返回错误()
    {
        var (result, _) = InvokeCommand(new LdrDmntService(), 0, new byte[10]);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrDmntService_SetProgramArgument2_带参数返回成功()
    {
        var args = "debug-arg\0"u8.ToArray();
        var data = new byte[16 + args.Length];
        BitConverter.GetBytes((uint)args.Length).CopyTo(data, 0);
        BitConverter.GetBytes(0xABAD1DEAUL).CopyTo(data, 8);
        args.CopyTo(data, 16);

        var service = new LdrDmntService();
        var (result, _) = InvokeCommand(service, 0, data);
        Assert.True(result.IsSuccess);

        var stored = service.GetProgramArgument(0xABAD1DEAUL);
        Assert.NotNull(stored);
        Assert.Equal("debug-arg", stored);
    }

    [Fact]
    public void LdrDmntService_GetProgramArgument_未设置返回null()
    {
        Assert.Null(new LdrDmntService().GetProgramArgument(0UL));
    }

    [Fact]
    public void LdrDmntService_FlushArguments2_返回成功()
    {
        var (result, response) = InvokeCommand(new LdrDmntService(), 1);
        Assert.True(result.IsSuccess);
        Assert.Empty(response.Data);
    }

    [Fact]
    public void LdrDmntService_GetProcessModuleInfo_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new LdrDmntService(), 2);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrDmntService_GetProcessModuleInfo_数据不足12字节返回错误()
    {
        var (result, _) = InvokeCommand(new LdrDmntService(), 2, new byte[8]);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrDmntService_GetProcessModuleInfo_无系统返回0模块()
    {
        // 12 bytes: u64 pid + s32 index
        var data = new byte[12];
        BitConverter.GetBytes(0x42UL).CopyTo(data, 0); // pid
        BitConverter.GetBytes(0).CopyTo(data, 8);       // index = 0

        var (result, response) = InvokeCommand(new LdrDmntService(), 2, data);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count); // s32 count = 0
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void LdrDmntService_GetProcessModuleInfo_index越界返回0模块()
    {
        var data = new byte[12];
        BitConverter.GetBytes(0x42UL).CopyTo(data, 0);
        BitConverter.GetBytes(1).CopyTo(data, 8); // index = 1 (越界，仅支持 index=0)

        var (result, response) = InvokeCommand(new LdrDmntService(), 2, data);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    // ──────────────────────────── LdrPmService (ldr:pm) ────────────────────────────

    [Fact]
    public void LdrPmService_PortName_是ldrPm()
    {
        Assert.Equal("ldr:pm", new LdrPmService().PortName);
    }

    [Fact]
    public void LdrPmService_命令表包含0到4()
    {
        var service = new LdrPmService();
        for (uint i = 0; i <= 4; i++)
            Assert.True(service.CommandTable.ContainsKey(i), $"缺少命令 {i}");
        Assert.Equal(5, service.CommandTable.Count);
    }

    [Fact]
    public void LdrPmService_未知命令返回错误()
    {
        var (result, _) = InvokeCommand(new LdrPmService(), 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrPmService_CreateProcess_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new LdrPmService(), 0);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrPmService_CreateProcess_数据不足24字节返回错误()
    {
        var (result, _) = InvokeCommand(new LdrPmService(), 0, new byte[20]);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrPmService_CreateProcess_未Pin返回LdrError6()
    {
        var data = new byte[24];
        BitConverter.GetBytes(1UL).CopyTo(data, 0); // pinId = 1 (未 Pin)
        BitConverter.GetBytes(0U).CopyTo(data, 8);   // flags

        var (result, _) = InvokeCommand(new LdrPmService(), 0, data);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.LdrResult(6), result); // Not pinned
    }

    [Fact]
    public void LdrPmService_PinProgram_返回递增PinId()
    {
        var service = new LdrPmService();

        var data1 = new byte[8];
        BitConverter.GetBytes(0x1000UL).CopyTo(data1, 0);
        var (result1, response1) = InvokeCommand(service, 2, data1);
        Assert.True(result1.IsSuccess);
        Assert.Equal(8, response1.Data.Count);
        Assert.Equal(1UL, BitConverter.ToUInt64(response1.Data.ToArray(), 0)); // pinId = 1

        var data2 = new byte[8];
        BitConverter.GetBytes(0x2000UL).CopyTo(data2, 0);
        var (result2, response2) = InvokeCommand(service, 2, data2);
        Assert.True(result2.IsSuccess);
        Assert.Equal(2UL, BitConverter.ToUInt64(response2.Data.ToArray(), 0)); // pinId = 2
    }

    [Fact]
    public void LdrPmService_PinProgram_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new LdrPmService(), 2);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrPmService_PinThenCreateProcess_返回虚拟句柄()
    {
        var service = new LdrPmService();

        // 1. Pin 程序
        var pinData = new byte[8];
        BitConverter.GetBytes(0xABCDUL).CopyTo(pinData, 0);
        var (pinResult, pinResponse) = InvokeCommand(service, 2, pinData);
        Assert.True(pinResult.IsSuccess);
        ulong pinId = BitConverter.ToUInt64(pinResponse.Data.ToArray(), 0);

        // 2. CreateProcess 使用 pinId
        var createData = new byte[24];
        BitConverter.GetBytes(pinId).CopyTo(createData, 0);   // pinId
        BitConverter.GetBytes(0U).CopyTo(createData, 8);       // flags
        var (createResult, createResponse) = InvokeCommand(service, 0, createData);
        Assert.True(createResult.IsSuccess);
        Assert.Equal(4, createResponse.Data.Count); // int32 handle

        int handle = BitConverter.ToInt32(createResponse.Data.ToArray(), 0);
        // 验证句柄范围: 0xFFFF1000 + (pinId & 0xFFF)
        Assert.Equal(unchecked((int)(0xFFFF1000U + (uint)(pinId & 0xFFFU))), handle);
    }

    [Fact]
    public void LdrPmService_UnpinProgram_成功移除()
    {
        var service = new LdrPmService();

        // 先 Pin
        var pinData = new byte[8];
        BitConverter.GetBytes(0x5000UL).CopyTo(pinData, 0);
        var (pinResult, pinResponse) = InvokeCommand(service, 2, pinData);
        Assert.True(pinResult.IsSuccess);
        ulong pinId = BitConverter.ToUInt64(pinResponse.Data.ToArray(), 0);

        // Unpin
        var unpinData = new byte[8];
        BitConverter.GetBytes(pinId).CopyTo(unpinData, 0);
        var (unpinResult, _) = InvokeCommand(service, 3, unpinData);
        Assert.True(unpinResult.IsSuccess);

        // 再次 CreateProcess 应该失败（已被 Unpin）
        var createData = new byte[24];
        BitConverter.GetBytes(pinId).CopyTo(createData, 0);
        BitConverter.GetBytes(0U).CopyTo(createData, 8);
        var (createResult, _) = InvokeCommand(service, 0, createData);
        Assert.False(createResult.IsSuccess);
    }

    [Fact]
    public void LdrPmService_UnpinProgram_无效PinId返回错误()
    {
        var unpinData = new byte[8];
        BitConverter.GetBytes(9999UL).CopyTo(unpinData, 0);
        var (result, _) = InvokeCommand(new LdrPmService(), 3, unpinData);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.LdrResult(6), result); // Not pinned
    }

    [Fact]
    public void LdrPmService_UnpinProgram_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new LdrPmService(), 3);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrPmService_GetProgramInfo_返回正确布局()
    {
        var (result, response) = InvokeCommand(new LdrPmService(), 1);
        Assert.True(result.IsSuccess);

        // ProgramInfo = 1+1+2+4+8+4+4+4+4 = 32 bytes (0x20)
        Assert.Equal(32, response.Data.Count);

        var data = response.Data.ToArray();
        Assert.Equal(44, data[0]);   // MainThreadPriority
        Assert.Equal(0, data[1]);    // DefaultCpuId

        ushort flags = BitConverter.ToUInt16(data, 2);
        Assert.Equal(0, flags);

        uint stackSize = BitConverter.ToUInt32(data, 4);
        Assert.Equal(0x100000U, stackSize); // 1MB

        ulong programId = BitConverter.ToUInt64(data, 8);
        Assert.Equal(0UL, programId);

        // SAC/FAC sizes 全零
        Assert.Equal(0, BitConverter.ToInt32(data, 16)); // AcidSacSize
        Assert.Equal(0, BitConverter.ToInt32(data, 20)); // AciSacSize
        Assert.Equal(0, BitConverter.ToInt32(data, 24)); // AcidFacSize
        Assert.Equal(0, BitConverter.ToInt32(data, 28)); // AciFacSize
    }

    [Fact]
    public void LdrPmService_SetEnabledProgramVerification_关闭()
    {
        var service = new LdrPmService();
        Assert.True(service.IsProgramVerificationEnabled); // 默认启用

        var (result, _) = InvokeCommand(service, 4, [0]); // enabled = false
        Assert.True(result.IsSuccess);
        Assert.False(service.IsProgramVerificationEnabled);
    }

    [Fact]
    public void LdrPmService_SetEnabledProgramVerification_开启()
    {
        var service = new LdrPmService();

        // 先关闭
        InvokeCommand(service, 4, [0]);
        Assert.False(service.IsProgramVerificationEnabled);

        // 再开启
        var (result, _) = InvokeCommand(service, 4, [1]);
        Assert.True(result.IsSuccess);
        Assert.True(service.IsProgramVerificationEnabled);
    }

    [Fact]
    public void LdrPmService_SetEnabledProgramVerification_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new LdrPmService(), 4);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LdrPmService_SetEnabledProgramVerification_非零值视为启用()
    {
        var service = new LdrPmService();
        InvokeCommand(service, 4, [0]); // 关闭

        InvokeCommand(service, 4, [0xFF]); // 任意非零 → 启用
        Assert.True(service.IsProgramVerificationEnabled);
    }

    // ──────────────────────────── IpcServiceManager 集成 ────────────────────────────

    [Fact]
    public void IpcServiceManager_LdrShelService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var service = new LdrShelService();
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("ldr:shel"));
    }

    [Fact]
    public void IpcServiceManager_LdrDmntService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var service = new LdrDmntService();
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("ldr:dmnt"));
    }

    [Fact]
    public void IpcServiceManager_LdrPmService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var service = new LdrPmService();
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("ldr:pm"));
    }

    // ──────────────────────────── IDisposable ────────────────────────────

    [Fact]
    public void LdrShelService_Dispose_不抛异常()
    {
        var service = new LdrShelService();
        service.Dispose();
    }

    [Fact]
    public void LdrDmntService_Dispose_不抛异常()
    {
        var service = new LdrDmntService();
        service.Dispose();
    }

    [Fact]
    public void LdrPmService_Dispose_不抛异常()
    {
        var service = new LdrPmService();
        service.Dispose();
    }

    // ──────────────────────────── 辅助方法 ────────────────────────────

    /// <summary>构建 SetProgramArgument / SetProgramArgument2 的输入数据</summary>
    private static byte[] BuildSetArgData(ulong programId, string args)
    {
        var argBytes = Encoding.UTF8.GetBytes(args + "\0");
        var data = new byte[16 + argBytes.Length];
        BitConverter.GetBytes((uint)argBytes.Length).CopyTo(data, 0);
        BitConverter.GetBytes(programId).CopyTo(data, 8);
        argBytes.CopyTo(data, 16);
        return data;
    }
}
