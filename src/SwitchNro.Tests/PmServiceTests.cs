using System;
using SwitchNro.Common;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using Xunit;

using static SwitchNro.Tests.IpcTestHelper;

namespace SwitchNro.Tests;

/// <summary>
/// 进程管理服务单元测试 — PmDmntService, PmInfoService, PmShellService, PmBmService
/// </summary>
public class PmServiceTests
{
    // ──────────────────────────── PmDmntService (pm:dmnt) ────────────────────────────

    [Fact]
    public void PmDmntService_PortName_是pmDmnt()
    {
        Assert.Equal("pm:dmnt", new PmDmntService().PortName);
    }

    [Fact]
    public void PmDmntService_命令表包含0到7()
    {
        var service = new PmDmntService();
        for (uint i = 0; i <= 7; i++)
            Assert.True(service.CommandTable.ContainsKey(i), $"缺少命令 {i}");
    }

    [Fact]
    public void PmDmntService_未知命令返回错误()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_GetModuleIdList_返回成功()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 0);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_GetJitDebugProcessIdList_返回空列表()
    {
        var (result, response) = InvokeCommand(new PmDmntService(), 1);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count); // int32 count = 0
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void PmDmntService_StartProcess_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 2);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_StartProcess_有效PID返回成功()
    {
        var pid = BitConverter.GetBytes(0x42UL);
        var (result, _) = InvokeCommand(new PmDmntService(), 2, pid);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_GetProcessId_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 3);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_GetProcessId_无HorizonSystem返回PmError()
    {
        var titleId = BitConverter.GetBytes(0x0100000000010000UL);
        var (result, _) = InvokeCommand(new PmDmntService(), 3, titleId);
        // 无 HorizonSystem → pid = 0 → PmResult(2)
        Assert.False(result.IsSuccess);
        Assert.Equal(15, result.Module); // PM module = 15
    }

    [Fact]
    public void PmDmntService_HookToCreateProcess_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 4);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_HookToCreateProcess_返回事件句柄()
    {
        var titleId = BitConverter.GetBytes(0x0100000000010000UL);
        var (result, response) = InvokeCommand(new PmDmntService(), 4, titleId);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        var handle = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(unchecked((int)0xFFFF0001), handle);
    }

    [Fact]
    public void PmDmntService_GetApplicationProcessId_无系统返回PmError()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 5);
        Assert.False(result.IsSuccess);
        Assert.Equal(15, result.Module);
    }

    [Fact]
    public void PmDmntService_HookToCreateApplicationProcess_无数据返回事件句柄()
    {
        var (result, response) = InvokeCommand(new PmDmntService(), 6);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        var handle = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(unchecked((int)0xFFFF0002), handle);
    }

    [Fact]
    public void PmDmntService_ClearHook_有数据返回成功()
    {
        var flags = BitConverter.GetBytes(0x3U);
        var (result, response) = InvokeCommand(new PmDmntService(), 6, flags);
        Assert.True(result.IsSuccess);
        Assert.Empty(response.Data); // ClearHook 不返回数据
    }

    [Fact]
    public void PmDmntService_GetProgramId_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmDmntService(), 7);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmDmntService_GetProgramId_无系统返回PmError()
    {
        var pid = BitConverter.GetBytes(0x1UL);
        var (result, _) = InvokeCommand(new PmDmntService(), 7, pid);
        Assert.False(result.IsSuccess);
        Assert.Equal(15, result.Module);
    }

    // ──────────────────────────── PmInfoService (pm:info) ────────────────────────────

    [Fact]
    public void PmInfoService_PortName_是pmInfo()
    {
        Assert.Equal("pm:info", new PmInfoService().PortName);
    }

    [Fact]
    public void PmInfoService_命令表包含0到2()
    {
        var service = new PmInfoService();
        Assert.True(service.CommandTable.ContainsKey(0));
        Assert.True(service.CommandTable.ContainsKey(1));
        Assert.True(service.CommandTable.ContainsKey(2));
    }

    [Fact]
    public void PmInfoService_GetProgramId_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmInfoService(), 0);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmInfoService_GetProgramId_无系统返回PmError()
    {
        var pid = BitConverter.GetBytes(0x1UL);
        var (result, _) = InvokeCommand(new PmInfoService(), 0, pid);
        Assert.False(result.IsSuccess);
        Assert.Equal(15, result.Module);
    }

    [Fact]
    public void PmInfoService_GetAppletCurrentResourceLimitValues_返回正确数据()
    {
        var (result, response) = InvokeCommand(new PmInfoService(), 1);
        Assert.True(result.IsSuccess);
        // 8 bytes memory + 4 bytes threads + 8 bytes events = 20
        Assert.Equal(20, response.Data.Count);

        var data = response.Data.ToArray();
        var memLimit = BitConverter.ToUInt64(data, 0);
        var threadLimit = BitConverter.ToUInt32(data, 8);
        var eventLimit = BitConverter.ToUInt64(data, 12);
        Assert.Equal(0x1A000000UL, memLimit);
        Assert.Equal(512U, threadLimit);
        Assert.Equal(0UL, eventLimit);
    }

    [Fact]
    public void PmInfoService_GetAppletPeakResourceLimitValues_返回正确数据()
    {
        var (result, response) = InvokeCommand(new PmInfoService(), 2);
        Assert.True(result.IsSuccess);
        Assert.Equal(20, response.Data.Count);

        var data = response.Data.ToArray();
        var memPeak = BitConverter.ToUInt64(data, 0);
        var threadPeak = BitConverter.ToUInt32(data, 8);
        Assert.Equal(0x1A000000UL, memPeak);
        Assert.Equal(512U, threadPeak);
    }

    // ──────────────────────────── PmShellService (pm:shell) ────────────────────────────

    [Fact]
    public void PmShellService_PortName_是pmShell()
    {
        Assert.Equal("pm:shell", new PmShellService().PortName);
    }

    [Fact]
    public void PmShellService_命令表包含0到10()
    {
        var service = new PmShellService();
        for (uint i = 0; i <= 10; i++)
            Assert.True(service.CommandTable.ContainsKey(i), $"缺少命令 {i}");
    }

    [Fact]
    public void PmShellService_LaunchProgram_数据不足返回错误()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 0, BitConverter.GetBytes(0x42UL));
        Assert.False(result.IsSuccess); // 需 16 字节，仅 8 字节
    }

    [Fact]
    public void PmShellService_LaunchProgram_返回递增PID()
    {
        var data = new byte[16];
        BitConverter.GetBytes(0U).CopyTo(data, 0);        // launchFlags
        BitConverter.GetBytes(0x0100000000010000UL).CopyTo(data, 8); // programId

        var service = new PmShellService();
        var (result1, resp1) = InvokeCommand(service, 0, data);
        Assert.True(result1.IsSuccess);
        var pid1 = BitConverter.ToUInt64(resp1.Data.ToArray(), 0);

        var (result2, resp2) = InvokeCommand(service, 0, data);
        Assert.True(result2.IsSuccess);
        var pid2 = BitConverter.ToUInt64(resp2.Data.ToArray(), 0);

        Assert.True(pid2 > pid1, "LaunchProgram 应返回递增的 PID");
    }

    [Fact]
    public void PmShellService_TerminateProcess_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 1);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_TerminateProcess_有效PID返回成功()
    {
        var pid = BitConverter.GetBytes(0x42UL);
        var (result, _) = InvokeCommand(new PmShellService(), 1, pid);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_TerminateProgram_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 2);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_TerminateProgram_有效PID返回成功()
    {
        var pid = BitConverter.GetBytes(0x42UL);
        var (result, _) = InvokeCommand(new PmShellService(), 2, pid);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_GetProcessEventHandle_返回句柄()
    {
        var (result, response) = InvokeCommand(new PmShellService(), 3);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        var handle = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(unchecked((int)0xFFFF0100), handle);
    }

    [Fact]
    public void PmShellService_GetProcessEventInfo_无系统返回Created事件()
    {
        var (result, response) = InvokeCommand(new PmShellService(), 4);
        Assert.True(result.IsSuccess);
        // uint32 eventType + ulong pid = 12 bytes
        Assert.Equal(12, response.Data.Count);
        var data = response.Data.ToArray();
        var eventType = BitConverter.ToUInt32(data, 0);
        var pid = BitConverter.ToUInt64(data, 4);
        Assert.Equal(0U, eventType); // PmProcessEventInfo.Created = 0
        Assert.Equal(0UL, pid);
    }

    [Fact]
    public void PmShellService_CleanupProcess_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 5);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_CleanupProcess_有效PID返回成功()
    {
        var pid = BitConverter.GetBytes(0x42UL);
        var (result, _) = InvokeCommand(new PmShellService(), 5, pid);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_ClearJitDebugOccured_空数据返回错误()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 6);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_ClearJitDebugOccured_有效PID返回成功()
    {
        var pid = BitConverter.GetBytes(0x42UL);
        var (result, _) = InvokeCommand(new PmShellService(), 6, pid);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_NotifyBootFinished_设置状态()
    {
        var service = new PmShellService();
        Assert.False(service.IsBootFinished);

        var (result, _) = InvokeCommand(service, 7);
        Assert.True(result.IsSuccess);
        Assert.True(service.IsBootFinished);
    }

    [Fact]
    public void PmShellService_GetApplicationProcessIdForShell_无系统返回PmError()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 8);
        Assert.False(result.IsSuccess);
        Assert.Equal(15, result.Module);
    }

    [Fact]
    public void PmShellService_BoostApplicationThreadResourceLimit_返回成功()
    {
        var (result, _) = InvokeCommand(new PmShellService(), 9);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PmShellService_GetBootFinishedEventHandle_返回句柄()
    {
        var (result, response) = InvokeCommand(new PmShellService(), 10);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        var handle = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(unchecked((int)0xFFFF0200), handle);
    }

    // ──────────────────────────── PmBmService (pm:bm) ────────────────────────────

    [Fact]
    public void PmBmService_PortName_是pmBm()
    {
        Assert.Equal("pm:bm", new PmBmService().PortName);
    }

    [Fact]
    public void PmBmService_命令表包含0和1()
    {
        var service = new PmBmService();
        Assert.True(service.CommandTable.ContainsKey(0));
        Assert.True(service.CommandTable.ContainsKey(1));
    }

    [Fact]
    public void PmBmService_GetBootMode_默认Normal()
    {
        var (result, response) = InvokeCommand(new PmBmService(), 0);
        Assert.True(result.IsSuccess);
        Assert.Single(response.Data);
        Assert.Equal((byte)0, response.Data[0]); // BootMode.Normal = 0
    }

    [Fact]
    public void PmBmService_SetMaintenanceBoot_变更启动模式()
    {
        var service = new PmBmService();

        // 先设置为维护模式
        var (resultSet, _) = InvokeCommand(service, 1);
        Assert.True(resultSet.IsSuccess);

        // 再查询，应为 Maintenance = 1
        var (resultGet, responseGet) = InvokeCommand(service, 0);
        Assert.True(resultGet.IsSuccess);
        Assert.Equal((byte)1, responseGet.Data[0]); // BootMode.Maintenance = 1
    }

    // ──────────────────────────── IpcServiceManager 集成 ────────────────────────────

    [Fact]
    public void IpcServiceManager_注册Pm服务后可查找()
    {
        var manager = new IpcServiceManager();
        manager.RegisterService(new PmDmntService());
        manager.RegisterService(new PmInfoService());
        manager.RegisterService(new PmShellService());
        manager.RegisterService(new PmBmService());

        Assert.NotNull(manager.GetService("pm:dmnt"));
        Assert.NotNull(manager.GetService("pm:info"));
        Assert.NotNull(manager.GetService("pm:shell"));
        Assert.NotNull(manager.GetService("pm:bm"));
    }

    [Fact]
    public void PmServices_全部实现IDisposable()
    {
        IIpcService[] services =
        [
            new PmDmntService(),
            new PmInfoService(),
            new PmShellService(),
            new PmBmService(),
        ];

        foreach (var svc in services)
        {
            svc.Dispose(); // 不应抛异常
            Assert.NotNull(svc.PortName);
        }
    }
}
