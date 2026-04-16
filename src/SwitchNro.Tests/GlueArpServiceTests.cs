using System;
using System.Text;
using SwitchNro.Common;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using Xunit;

using static SwitchNro.Tests.IpcTestHelper;

namespace SwitchNro.Tests;

/// <summary>
/// ARP Glue 服务单元测试 — ArpRService, ArpWService, ArpRegistry
/// </summary>
public class GlueArpServiceTests
{
    /// <summary>创建共享的 ArpRegistry + 服务实例</summary>
    private static (ArpRegistry Registry, ArpRService Reader, ArpWService Writer) CreateArpServices()
    {
        var registry = new ArpRegistry();
        var reader = new ArpRService(registry);
        var writer = new ArpWService(registry);
        return (registry, reader, writer);
    }

    // ──────────────────────────── ArpRegistry ────────────────────────────

    [Fact]
    public void ArpRegistry_注册返回条目()
    {
        var registry = new ArpRegistry();
        var entry = registry.Register(0x100, 0x01000UL);
        Assert.Equal(0x100UL, entry.ProcessId);
        Assert.Equal(0x01000UL, entry.ProgramId);
        Assert.Equal(0x100UL, entry.InstanceId); // 从 0x100 开始
        Assert.Equal(0U, entry.LaunchFlags);
        Assert.Equal(ArpLaunchMode.Application, entry.LaunchMode);
    }

    [Fact]
    public void ArpRegistry_注册递增InstanceId()
    {
        var registry = new ArpRegistry();
        var e1 = registry.Register(0x100, 0x1000UL);
        var e2 = registry.Register(0x200, 0x2000UL);
        Assert.Equal(0x100UL, e1.InstanceId);
        Assert.Equal(0x101UL, e2.InstanceId);
    }

    [Fact]
    public void ArpRegistry_注册带参数()
    {
        var registry = new ArpRegistry();
        var entry = registry.Register(0x42, 0xABCDUL, launchFlags: 0x3, launchMode: ArpLaunchMode.GameCard);
        Assert.Equal(0x3U, entry.LaunchFlags);
        Assert.Equal(ArpLaunchMode.GameCard, entry.LaunchMode);
    }

    [Fact]
    public void ArpRegistry_FindByProcessId_找到()
    {
        var registry = new ArpRegistry();
        registry.Register(0x100, 0x1000UL);
        var found = registry.FindByProcessId(0x100);
        Assert.NotNull(found);
        Assert.Equal(0x1000UL, found.ProgramId);
    }

    [Fact]
    public void ArpRegistry_FindByProcessId_未找到返回null()
    {
        Assert.Null(new ArpRegistry().FindByProcessId(0x999));
    }

    [Fact]
    public void ArpRegistry_FindByInstanceId_找到()
    {
        var registry = new ArpRegistry();
        var entry = registry.Register(0x100, 0x1000UL);
        var found = registry.FindByInstanceId(entry.InstanceId);
        Assert.NotNull(found);
        Assert.Equal(0x100UL, found.ProcessId);
    }

    [Fact]
    public void ArpRegistry_FindByInstanceId_未找到返回null()
    {
        Assert.Null(new ArpRegistry().FindByInstanceId(0xFFFF));
    }

    [Fact]
    public void ArpRegistry_Unregister_成功()
    {
        var registry = new ArpRegistry();
        var entry = registry.Register(0x100, 0x1000UL);
        Assert.True(registry.Unregister(entry.InstanceId));
        Assert.Null(registry.FindByInstanceId(entry.InstanceId));
    }

    [Fact]
    public void ArpRegistry_Unregister_无效InstanceId返回false()
    {
        Assert.False(new ArpRegistry().Unregister(0xDEAD));
    }

    [Fact]
    public void ArpRegistry_GetAllInstances_返回所有条目()
    {
        var registry = new ArpRegistry();
        registry.Register(0x100, 0x1000UL);
        registry.Register(0x200, 0x2000UL);
        Assert.Equal(2, registry.GetAllInstances().Count);
    }

    [Fact]
    public void ArpRegistry_ControlProperty_可设置()
    {
        var registry = new ArpRegistry();
        var entry = registry.Register(0x100, 0x1000UL);
        Assert.Null(entry.ControlProperty);
        entry.ControlProperty = new byte[] { 1, 2, 3 };
        Assert.Equal(new byte[] { 1, 2, 3 }, entry.ControlProperty);
    }

    // ──────────────────────────── ArpRService (arp:r) ────────────────────────────

    [Fact]
    public void ArpRService_PortName_是arpR()
    {
        var (_, reader, _) = CreateArpServices();
        Assert.Equal("arp:r", reader.PortName);
    }

    [Fact]
    public void ArpRService_命令表包含0到5()
    {
        var (_, reader, _) = CreateArpServices();
        for (uint i = 0; i <= 5; i++)
            Assert.True(reader.CommandTable.ContainsKey(i), $"缺少命令 {i}");
        Assert.Equal(6, reader.CommandTable.Count);
    }

    [Fact]
    public void ArpRService_未知命令返回错误()
    {
        var (_, reader, _) = CreateArpServices();
        var (result, _) = InvokeCommand(reader, 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ArpRService_GetApplicationLaunchProperty_空数据返回ArpError()
    {
        var (_, reader, _) = CreateArpServices();
        var (result, _) = InvokeCommand(reader, 0);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(2), result);
    }

    [Fact]
    public void ArpRService_GetApplicationLaunchProperty_未注册返回ArpError4()
    {
        var (_, reader, _) = CreateArpServices();
        var pid = BitConverter.GetBytes(0x999UL);
        var (result, _) = InvokeCommand(reader, 0, pid);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(4), result);
    }

    [Fact]
    public void ArpRService_GetApplicationLaunchProperty_已注册返回正确布局()
    {
        var (registry, reader, _) = CreateArpServices();
        registry.Register(0x42, 0xABCDUL, launchFlags: 0x7, launchMode: ArpLaunchMode.Download);

        var pid = BitConverter.GetBytes(0x42UL);
        var (result, response) = InvokeCommand(reader, 0, pid);
        Assert.True(result.IsSuccess);

        // ProgramId(8) + LaunchFlags(4) + LaunchMode(4) = 16 bytes
        Assert.Equal(16, response.Data.Count);
        var data = response.Data.ToArray();
        Assert.Equal(0xABCDUL, BitConverter.ToUInt64(data, 0));
        Assert.Equal(0x7U, BitConverter.ToUInt32(data, 8));
        Assert.Equal((uint)ArpLaunchMode.Download, BitConverter.ToUInt32(data, 12));
    }

    [Fact]
    public void ArpRService_GetApplicationControlProperty_未注册返回错误()
    {
        var (_, reader, _) = CreateArpServices();
        var pid = BitConverter.GetBytes(0x999UL);
        var (result, _) = InvokeCommand(reader, 1, pid);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(4), result);
    }

    [Fact]
    public void ArpRService_GetApplicationControlProperty_有自定义NACP返回()
    {
        var (registry, reader, _) = CreateArpServices();
        var entry = registry.Register(0x42, 0xABCDUL);
        entry.ControlProperty = Encoding.UTF8.GetBytes("MyApp\0");

        var pid = BitConverter.GetBytes(0x42UL);
        var (result, response) = InvokeCommand(reader, 1, pid);
        Assert.True(result.IsSuccess);
        Assert.True(response.Data.Count > 0);
    }

    [Fact]
    public void ArpRService_GetApplicationControlProperty_无自定义NACP返回默认()
    {
        var (registry, reader, _) = CreateArpServices();
        registry.Register(0x42, 0xABCDUL);

        var pid = BitConverter.GetBytes(0x42UL);
        var (result, response) = InvokeCommand(reader, 1, pid);
        Assert.True(result.IsSuccess);
        Assert.Equal(0x300, response.Data.Count); // 简化 NACP 大小
    }

    [Fact]
    public void ArpRService_GetApplicationProcessProperty_通过InstanceId查找()
    {
        var (registry, reader, _) = CreateArpServices();
        var entry = registry.Register(0x42, 0xABCDUL);

        var instanceId = BitConverter.GetBytes(entry.InstanceId);
        var (result, response) = InvokeCommand(reader, 2, instanceId);
        Assert.True(result.IsSuccess);

        // ProcessId(8) + ProgramId(8) = 16 bytes
        Assert.Equal(16, response.Data.Count);
        var data = response.Data.ToArray();
        Assert.Equal(0x42UL, BitConverter.ToUInt64(data, 0));
        Assert.Equal(0xABCDUL, BitConverter.ToUInt64(data, 8));
    }

    [Fact]
    public void ArpRService_GetApplicationProcessProperty_未注册返回错误()
    {
        var (_, reader, _) = CreateArpServices();
        var instanceId = BitConverter.GetBytes(0xDEADUL);
        var (result, _) = InvokeCommand(reader, 2, instanceId);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(4), result);
    }

    [Fact]
    public void ArpRService_GetApplicationInstanceId_通过ProcessId查找()
    {
        var (registry, reader, _) = CreateArpServices();
        var entry = registry.Register(0x42, 0xABCDUL);

        var pid = BitConverter.GetBytes(0x42UL);
        var (result, response) = InvokeCommand(reader, 3, pid);
        Assert.True(result.IsSuccess);
        Assert.Equal(8, response.Data.Count);
        Assert.Equal(entry.InstanceId, BitConverter.ToUInt64(response.Data.ToArray(), 0));
    }

    [Fact]
    public void ArpRService_GetApplicationInstanceId_未注册返回错误()
    {
        var (_, reader, _) = CreateArpServices();
        var pid = BitConverter.GetBytes(0x999UL);
        var (result, _) = InvokeCommand(reader, 3, pid);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ArpRService_GetApplicationInstanceUnregistrationNotifier_返回事件句柄()
    {
        var (_, reader, _) = CreateArpServices();
        var (result, response) = InvokeCommand(reader, 4);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        var handle = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(unchecked((int)0xFFFF0300), handle);
    }

    [Fact]
    public void ArpRService_ListApplicationInstanceId_空注册表返回0()
    {
        var (_, reader, _) = CreateArpServices();
        var (result, response) = InvokeCommand(reader, 5);
        Assert.True(result.IsSuccess);
        // 只有 s32 count = 4 bytes
        Assert.Equal(4, response.Data.Count);
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void ArpRService_ListApplicationInstanceId_有注册返回实例ID列表()
    {
        var (registry, reader, _) = CreateArpServices();
        registry.Register(0x100, 0x1000UL);
        registry.Register(0x200, 0x2000UL);

        var (result, response) = InvokeCommand(reader, 5);
        Assert.True(result.IsSuccess);
        // 2 * 8 bytes (InstanceId) + 4 bytes (count) = 20 bytes
        Assert.Equal(20, response.Data.Count);
        var data = response.Data.ToArray();
        Assert.Equal(0x100UL, BitConverter.ToUInt64(data, 0));
        Assert.Equal(0x101UL, BitConverter.ToUInt64(data, 8));
        Assert.Equal(2, BitConverter.ToInt32(data, 16));
    }

    // ──────────────────────────── ArpWService (arp:w) ────────────────────────────

    [Fact]
    public void ArpWService_PortName_是arpW()
    {
        var (_, _, writer) = CreateArpServices();
        Assert.Equal("arp:w", writer.PortName);
    }

    [Fact]
    public void ArpWService_命令表包含0和1()
    {
        var (_, _, writer) = CreateArpServices();
        Assert.True(writer.CommandTable.ContainsKey(0)); // AcquireRegistrar
        Assert.True(writer.CommandTable.ContainsKey(1)); // UnregisterApplicationInstance
        Assert.Equal(2, writer.CommandTable.Count);
    }

    [Fact]
    public void ArpWService_未知命令返回错误()
    {
        var (_, _, writer) = CreateArpServices();
        var (result, _) = InvokeCommand(writer, 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ArpWService_AcquireRegistrar_返回注册器句柄()
    {
        var (_, _, writer) = CreateArpServices();
        var (result, response) = InvokeCommand(writer, 0);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
        var handle = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(unchecked((int)0xFFFF0400), handle);
    }

    [Fact]
    public void ArpWService_UnregisterApplicationInstance_空数据返回ArpError()
    {
        var (_, _, writer) = CreateArpServices();
        var (result, _) = InvokeCommand(writer, 1);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(2), result);
    }

    [Fact]
    public void ArpWService_UnregisterApplicationInstance_无效InstanceId返回ArpError4()
    {
        var (_, _, writer) = CreateArpServices();
        var instanceId = BitConverter.GetBytes(0xDEADUL);
        var (result, _) = InvokeCommand(writer, 1, instanceId);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(4), result);
    }

    [Fact]
    public void ArpWService_UnregisterApplicationInstance_成功注销()
    {
        var (registry, _, writer) = CreateArpServices();
        var entry = registry.Register(0x42, 0xABCDUL);

        var instanceId = BitConverter.GetBytes(entry.InstanceId);
        var (result, _) = InvokeCommand(writer, 1, instanceId);
        Assert.True(result.IsSuccess);
        Assert.Null(registry.FindByInstanceId(entry.InstanceId));
    }

    // ──────────────────────────── arp:r + arp:w 交互 ────────────────────────────

    [Fact]
    public void ArpR和ArpW_共享同一注册表_注册后可读取()
    {
        var (registry, reader, writer) = CreateArpServices();

        // 通过注册表直接注册
        var entry = registry.Register(0x50, 0x5000UL, launchFlags: 1, launchMode: ArpLaunchMode.Application2);

        // 通过 arp:r 读取
        var pid = BitConverter.GetBytes(0x50UL);
        var (result, response) = InvokeCommand(reader, 0, pid);
        Assert.True(result.IsSuccess);
        var data = response.Data.ToArray();
        Assert.Equal(0x5000UL, BitConverter.ToUInt64(data, 0));
        Assert.Equal(1U, BitConverter.ToUInt32(data, 8));
        Assert.Equal((uint)ArpLaunchMode.Application2, BitConverter.ToUInt32(data, 12));
    }

    [Fact]
    public void ArpR和ArpW_注销后ArpR查询失败()
    {
        var (registry, reader, writer) = CreateArpServices();
        var entry = registry.Register(0x50, 0x5000UL);

        // 通过 arp:w 注销
        var instanceId = BitConverter.GetBytes(entry.InstanceId);
        InvokeCommand(writer, 1, instanceId);

        // 通过 arp:r 查询应失败
        var pid = BitConverter.GetBytes(0x50UL);
        var (result, _) = InvokeCommand(reader, 0, pid);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.ArpResult(4), result);
    }

    // ──────────────────────────── IpcServiceManager 集成 ────────────────────────────

    [Fact]
    public void IpcServiceManager_ArpRService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var registry = new ArpRegistry();
        var service = new ArpRService(registry);
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("arp:r"));
    }

    [Fact]
    public void IpcServiceManager_ArpWService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var registry = new ArpRegistry();
        var service = new ArpWService(registry);
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("arp:w"));
    }

    // ──────────────────────────── IDisposable ────────────────────────────

    [Fact]
    public void ArpRService_Dispose_不抛异常()
    {
        var (_, reader, _) = CreateArpServices();
        reader.Dispose();
    }

    [Fact]
    public void ArpWService_Dispose_不抛异常()
    {
        var (_, _, writer) = CreateArpServices();
        writer.Dispose();
    }

    // ──────────────────────────── ArpLaunchMode 枚举 ────────────────────────────

    [Fact]
    public void ArpLaunchMode_值正确()
    {
        Assert.Equal(0U, (uint)ArpLaunchMode.Application);
        Assert.Equal(1U, (uint)ArpLaunchMode.Application2);
        Assert.Equal(2U, (uint)ArpLaunchMode.GameCard);
        Assert.Equal(3U, (uint)ArpLaunchMode.Download);
    }
}
