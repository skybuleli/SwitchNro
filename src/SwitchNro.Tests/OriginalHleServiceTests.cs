using System;
using SwitchNro.Common;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using SwitchNro.Horizon;
using Xunit;

using static SwitchNro.Tests.IpcTestHelper;

namespace SwitchNro.Tests;

/// <summary>
/// 原始4个HLE服务单元测试 — SmService, FsService, ViService, HidService
/// </summary>
public class OriginalHleServiceTests
{

    // ──────────────────────────── SmService (sm:) ────────────────────────────

    [Fact]
    public void SmService_PortName_是sm冒号()
    {
        var manager = new IpcServiceManager();
        Assert.Equal("sm:", new SmService(manager).PortName);
    }

    [Fact]
    public void SmService_命令表包含0和1和2()
    {
        var service = new SmService(new IpcServiceManager());
        Assert.True(service.CommandTable.ContainsKey(0)); // Initialize
        Assert.True(service.CommandTable.ContainsKey(1)); // GetService
        Assert.True(service.CommandTable.ContainsKey(2)); // RegisterService
    }

    [Fact]
    public void SmService_初始化返回成功()
    {
        var service = new SmService(new IpcServiceManager());
        var (result, _) = InvokeCommand(service, 0);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SmService_获取已注册服务返回成功()
    {
        var manager = new IpcServiceManager();
        var fsService = new FsService();
        manager.RegisterService(fsService);
        manager.HandleTable = new HandleTable();

        var smService = new SmService(manager);
        var serviceName = System.Text.Encoding.ASCII.GetBytes("fs:\0");
        var (result, response) = InvokeCommand(smService, 1, serviceName);

        Assert.True(result.IsSuccess);
        Assert.Single(response.CopyHandles); // 返回一个句柄
    }

    [Fact]
    public void SmService_获取未注册服务返回错误()
    {
        var manager = new IpcServiceManager();
        var smService = new SmService(manager);

        var serviceName = System.Text.Encoding.ASCII.GetBytes("nonexistent\0");
        var (result, _) = InvokeCommand(smService, 1, serviceName);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SmService_注册服务返回成功()
    {
        var service = new SmService(new IpcServiceManager());
        var serviceName = System.Text.Encoding.ASCII.GetBytes("test:srv\0");
        var (result, _) = InvokeCommand(service, 2, serviceName);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SmService_多次获取服务返回不同句柄()
    {
        var manager = new IpcServiceManager();
        manager.RegisterService(new FsService());
        manager.HandleTable = new HandleTable();

        var smService = new SmService(manager);
        var serviceName = System.Text.Encoding.ASCII.GetBytes("fs:\0");

        var (_, resp1) = InvokeCommand(smService, 1, serviceName);
        var (_, resp2) = InvokeCommand(smService, 1, serviceName);

        // 每次调用应分配不同的句柄
        Assert.NotEqual(resp1.CopyHandles[0], resp2.CopyHandles[0]);
    }

    [Fact]
    public void SmService_获取多个已注册服务均成功()
    {
        var manager = new IpcServiceManager();
        manager.RegisterService(new FsService());
        manager.RegisterService(new ViService("vi:m"));
        manager.RegisterService(new HidService());
        manager.HandleTable = new HandleTable();

        var smService = new SmService(manager);

        foreach (var name in new[] { "fs:", "vi:m", "hid:" })
        {
            var serviceName = System.Text.Encoding.ASCII.GetBytes(name + "\0");
            var (result, _) = InvokeCommand(smService, 1, serviceName);
            Assert.True(result.IsSuccess, $"获取服务 {name} 应成功");
        }
    }

    // ──────────────────────────── FsService (fs:) ────────────────────────────

    [Fact]
    public void FsService_PortName_是fs冒号()
    {
        Assert.Equal("fs:", new FsService().PortName);
    }

    [Fact]
    public void FsService_命令表包含所有命令()
    {
        var service = new FsService();
        Assert.True(service.CommandTable.ContainsKey(0));   // OpenFileSystem
        Assert.True(service.CommandTable.ContainsKey(1));   // CreateFileSystem
        Assert.True(service.CommandTable.ContainsKey(8));   // MountContent
        Assert.True(service.CommandTable.ContainsKey(18));  // OpenSdCardFileSystem
        Assert.True(service.CommandTable.ContainsKey(51));  // IsSignedSystemPartitionOnSdCard
        Assert.True(service.CommandTable.ContainsKey(100)); // OpenBisFileSystem
    }

    [Fact]
    public void FsService_打开文件系统返回成功()
    {
        var service = new FsService();
        var (result, _) = InvokeCommand(service, 0);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FsService_创建文件系统返回成功()
    {
        var service = new FsService();
        var (result, _) = InvokeCommand(service, 1);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FsService_挂载内容返回成功()
    {
        var service = new FsService();
        var (result, _) = InvokeCommand(service, 8);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FsService_打开SD卡文件系统返回成功()
    {
        var service = new FsService();
        var (result, _) = InvokeCommand(service, 18);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FsService_SDCard签名分区检查返回false()
    {
        var service = new FsService();
        var (result, response) = InvokeCommand(service, 51);

        Assert.True(result.IsSuccess);
        Assert.Single(response.Data);
        Assert.Equal(0, response.Data[0]); // false — 不是签名分区
    }

    [Fact]
    public void FsService_打开Bis文件系统返回成功()
    {
        var service = new FsService();
        var (result, _) = InvokeCommand(service, 100);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FsService_未知命令返回错误()
    {
        var service = new FsService();
        var (result, _) = InvokeCommand(service, 999);

        Assert.False(result.IsSuccess);
    }

    // ──────────────────────────── ViService (vi:) ────────────────────────────

    [Fact]
    public void ViService_PortName_是vi冒号()
    {
        Assert.Equal("vi:m", new ViService().PortName);
    }

    [Fact]
    public void ViService_命令表包含100和101()
    {
        var service = new ViService();
        Assert.True(service.CommandTable.ContainsKey(100)); // GetDisplayService
        Assert.True(service.CommandTable.ContainsKey(101)); // GetDisplayService2
    }

    [Fact]
    public void ViService_获取显示服务返回成功()
    {
        var service = new ViService();
        var (result, _) = InvokeCommand(service, 100);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ViService_获取显示服务2返回成功()
    {
        var service = new ViService();
        var (result, _) = InvokeCommand(service, 101);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ViService_帧提交事件可以被订阅()
    {
        var service = new ViService();
        bool eventFired = false;
        int receivedWidth = 0, receivedHeight = 0;

        service.FramePresented += (w, h, _) =>
        {
            eventFired = true;
            receivedWidth = w;
            receivedHeight = h;
        };

        // 模拟提交一帧 1280x720
        var frameData = new byte[1280 * 720 * 4];
        service.PresentFrame(1280, 720, frameData);

        Assert.True(eventFired);
        Assert.Equal(1280, receivedWidth);
        Assert.Equal(720, receivedHeight);
    }

    [Fact]
    public void ViService_无订阅者时帧提交不抛异常()
    {
        var service = new ViService();
        var frameData = new byte[100];

        var ex = Record.Exception(() => service.PresentFrame(10, 10, frameData));
        Assert.Null(ex);
    }

    [Fact]
    public void ViService_未知命令返回错误()
    {
        var service = new ViService();
        var (result, _) = InvokeCommand(service, 999);
        Assert.False(result.IsSuccess);
    }

    // ──────────────────────────── HidService (hid:) ────────────────────────────

    [Fact]
    public void HidService_PortName_是hid冒号()
    {
        Assert.Equal("hid:", new HidService().PortName);
    }

    [Fact]
    public void HidService_命令表包含所有命令()
    {
        var service = new HidService();
        Assert.True(service.CommandTable.ContainsKey(0));   // CreateAppletResource
        Assert.True(service.CommandTable.ContainsKey(1));   // ActivateDebugPad
        Assert.True(service.CommandTable.ContainsKey(11));  // ActivateTouchScreen
        Assert.True(service.CommandTable.ContainsKey(66));  // StartSixAxisSensor
        Assert.True(service.CommandTable.ContainsKey(100)); // SetSupportedNpadStyleSet
        Assert.True(service.CommandTable.ContainsKey(101)); // GetSupportedNpadStyleSet
        Assert.True(service.CommandTable.ContainsKey(102)); // SetSupportedNpadIdType
        Assert.True(service.CommandTable.ContainsKey(120)); // SetNpadJoyHoldType
        Assert.True(service.CommandTable.ContainsKey(203)); // CreateActiveVibrationDevice
    }

    [Fact]
    public void HidService_创建Applet资源返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 0);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_激活调试手柄返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 1);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_激活触屏返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 11);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_启动六轴传感器返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 66);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_设置支持的手柄类型返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 100);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_获取支持的手柄类型返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 101);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_设置支持的手柄ID返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 102);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_设置JoyCon握持类型返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 120);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_创建振动设备返回成功()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 203);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void HidService_未知命令返回错误()
    {
        var service = new HidService();
        var (result, _) = InvokeCommand(service, 999);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void HidService_更新按键状态不抛异常()
    {
        var service = new HidService();
        var ex = Record.Exception(() => service.UpdateButtonState(0xABCD));
        Assert.Null(ex);
    }

    [Fact]
    public void HidService_更新左摇杆位置不抛异常()
    {
        var service = new HidService();
        var ex = Record.Exception(() => service.UpdateStickPosition(0, 0.5f, -0.3f));
        Assert.Null(ex);
    }

    [Fact]
    public void HidService_更新右摇杆位置不抛异常()
    {
        var service = new HidService();
        var ex = Record.Exception(() => service.UpdateStickPosition(1, -1.0f, 0.8f));
        Assert.Null(ex);
    }

    [Fact]
    public void HidService_更新触屏状态不抛异常()
    {
        var service = new HidService();
        var ex = Record.Exception(() => service.UpdateTouchState(0, 400, 200, true));
        Assert.Null(ex);
    }

    // ──────────────────────────── Dispose ────────────────────────────

    [Fact]
    public void 原始四个服务_Dispose不抛异常()
    {
        IIpcService[] services =
        [
            new SmService(new IpcServiceManager()),
            new FsService(),
            new ViService(),
            new HidService(),
        ];

        foreach (var service in services)
        {
            var ex = Record.Exception(() => service.Dispose());
            Assert.Null(ex);
        }
    }
}
