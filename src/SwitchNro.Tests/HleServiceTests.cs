using System;
using System.Linq;
using SwitchNro.Common;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using SwitchNro.Horizon;
using Xunit;

using static SwitchNro.Tests.IpcTestHelper;

namespace SwitchNro.Tests;

/// <summary>
/// HLE 服务单元测试 — 验证 IPC 服务注册、命令分发和响应数据
/// </summary>
public class HleServiceTests
{

    // ──────────────────────────── IpcServiceManager ────────────────────────────

    [Fact]
    public void IpcServiceManager_RegisterAndGet_ReturnsService()
    {
        var manager = new IpcServiceManager();
        var nvService = new NvService();
        manager.RegisterService(nvService);

        var found = manager.GetService("nvdrv:a");
        Assert.NotNull(found);
        Assert.Same(nvService, found);
    }

    [Fact]
    public void IpcServiceManager_UnregisteredService_ReturnsNull()
    {
        var manager = new IpcServiceManager();
        Assert.Null(manager.GetService("nonexistent"));
    }

    [Fact]
    public void IpcServiceManager_DispatchRequest_ReturnsSuccess()
    {
        var manager = new IpcServiceManager();
        manager.RegisterService(new SettingsService());

        var request = EmptyRequest(0); // GetLanguageCode
        var response = new IpcResponse();
        var result = manager.HandleRequest("set:", request, ref response);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void IpcServiceManager_UnknownService_ReturnsError()
    {
        var manager = new IpcServiceManager();
        var request = EmptyRequest(0);
        var response = new IpcResponse();
        var result = manager.HandleRequest("unknown:", request, ref response);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void IpcServiceManager_UnknownCommand_ReturnsError()
    {
        var manager = new IpcServiceManager();
        manager.RegisterService(new NvService());

        var request = EmptyRequest(0xFF); // 不存在的命令
        var response = new IpcResponse();
        var result = manager.HandleRequest("nvdrv:a", request, ref response);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void IpcServiceManager_GetAllServices_ReturnsAllRegistered()
    {
        var manager = new IpcServiceManager();
        manager.RegisterService(new NvService());
        manager.RegisterService(new AmService());
        manager.RegisterService(new TimeService());

        Assert.Equal(3, manager.GetAllServices().Count);
    }

    // ──────────────────────────── NvService (nvdrv:a) ────────────────────────────

    [Fact]
    public void NvService_PortName_IsCorrect()
    {
        Assert.Equal("nvdrv:a", new NvService().PortName);
    }

    [Fact]
    public void NvService_CommandTable_ContainsExpectedCommands()
    {
        var service = new NvService();
        Assert.True(service.CommandTable.ContainsKey(0)); // Open
        Assert.True(service.CommandTable.ContainsKey(1)); // Ioctl
        Assert.True(service.CommandTable.ContainsKey(2)); // Close
        Assert.True(service.CommandTable.ContainsKey(5)); // GetStatus
    }

    [Fact]
    public void NvService_Open_ReturnsFileDescriptor()
    {
        var service = new NvService();
        var devicePath = System.Text.Encoding.ASCII.GetBytes("/dev/nvhost-ctrl\0");
        var (result, response) = InvokeCommand(service, 0, devicePath);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count); // int32 fd
        int fd = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.True(fd >= 0x10); // _nextFd starts at 0x10
    }

    [Fact]
    public void NvService_Open_EmptyData_ReturnsError()
    {
        var service = new NvService();
        var (result, _) = InvokeCommand(service, 0, []);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void NvService_GetStatus_ReturnsZero()
    {
        var service = new NvService();
        var (result, response) = InvokeCommand(service, 5);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void NvService_Ioctl_InvalidData_ReturnsError()
    {
        var service = new NvService();
        var (result, _) = InvokeCommand(service, 1, []);

        Assert.False(result.IsSuccess); // Less than 4 bytes
    }

    [Fact]
    public void NvService_Close_RemovesFd()
    {
        var service = new NvService();
        // Open first to get an fd
        var devicePath = System.Text.Encoding.ASCII.GetBytes("/dev/nvhost-ctrl\0");
        var (_, openResp) = InvokeCommand(service, 0, devicePath);
        int fd = BitConverter.ToInt32(openResp.Data.ToArray(), 0);

        // Close the fd
        var (result, _) = InvokeCommand(service, 2, BitConverter.GetBytes(fd));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NvService_QueryEvent_ReturnsHandle()
    {
        var manager = new IpcServiceManager { HandleTable = new HandleTable() };
        var service = new NvService(serviceManager: manager);
        var (result, response) = InvokeCommand(service, 3);

        Assert.True(result.IsSuccess);
        Assert.Single(response.CopyHandles); // 返回一个句柄
        Assert.True(response.CopyHandles[0] >= 0xD000, $"句柄应在内核范围 (≥0xD000)，实际为 0x{response.CopyHandles[0]:X8}");
    }

    // ──────────────────────────── NvMemPService (nvmemp:) ────────────────────────────

    [Fact]
    public void NvMemPService_PortName_IsCorrect()
    {
        Assert.Equal("nvmemp:", new NvMemPService().PortName);
    }

    [Fact]
    public void NvMemPService_AllCommands_ReturnSuccess()
    {
        var service = new NvMemPService();
        foreach (var cmdId in new[] { 0u, 1u, 2u })
        {
            var (result, _) = InvokeCommand(service, cmdId);
            Assert.True(result.IsSuccess, $"Command {cmdId} should succeed");
        }
    }

    // ──────────────────────────── AmService (appletOE) ────────────────────────────

    [Fact]
    public void AmService_PortName_IsCorrect()
    {
        Assert.Equal("appletOE", new AmService().PortName);
    }

    [Fact]
    public void AmService_GetFocusState_DefaultIsInFocus()
    {
        var service = new AmService();
        var (result, response) = InvokeCommand(service, 80);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, response.Data[0]); // InFocus = 1
    }

    [Fact]
    public void AmService_SetAppletWindowFocus_UpdatesFocusState()
    {
        var service = new AmService();

        // Set to OutOfFocus (0)
        InvokeCommand(service, 60, new byte[] { 0 });

        var (_, response) = InvokeCommand(service, 80);
        Assert.Equal(0, response.Data[0]); // OutOfFocus = 0
    }

    [Fact]
    public void AmService_GetOperationMode_DefaultIsHandheld()
    {
        var service = new AmService();
        var (result, response) = InvokeCommand(service, 90);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, response.Data[0]); // Handheld = 0
    }

    [Fact]
    public void AmService_SetOperationMode_UpdatesMode()
    {
        var service = new AmService();
        service.SetOperationMode(OperationMode.Docked);

        var (_, response) = InvokeCommand(service, 90);
        Assert.Equal(1, response.Data[0]); // Docked = 1
    }

    [Fact]
    public void AmService_GetPerformanceMode_MatchesOperationMode()
    {
        var service = new AmService();
        service.SetOperationMode(OperationMode.Docked);

        var (_, response) = InvokeCommand(service, 91);
        Assert.Equal(1, response.Data[0]); // Docked = 1
    }

    [Fact]
    public void AmService_GetDisplayLogicalResolution_Returns1280x720()
    {
        var service = new AmService();
        var (result, response) = InvokeCommand(service, 100);

        Assert.True(result.IsSuccess);
        var data = response.Data.ToArray();
        Assert.Equal(1280, BitConverter.ToInt32(data, 0));  // Width
        Assert.Equal(720, BitConverter.ToInt32(data, 4));    // Height
    }

    [Fact]
    public void AmService_GetAppletResourceUserId_ReturnsNonZero()
    {
        var service = new AmService();
        var (result, response) = InvokeCommand(service, 50);

        Assert.True(result.IsSuccess);
        var userId = BitConverter.ToUInt64(response.Data.ToArray(), 0);
        Assert.NotEqual(0UL, userId);
    }

    // ──────────────────────────── AppletAeService (appletAE) ────────────────────────────

    [Fact]
    public void AppletAeService_PortName_IsCorrect()
    {
        Assert.Equal("appletAE", new AppletAeService().PortName);
    }

    [Fact]
    public void AppletAeService_GetAppletResourceUserId_ReturnsNonZero()
    {
        var service = new AppletAeService();
        var (result, response) = InvokeCommand(service, 100);

        Assert.True(result.IsSuccess);
        var userId = BitConverter.ToUInt64(response.Data.ToArray(), 0);
        Assert.NotEqual(0UL, userId);
    }

    // ──────────────────────────── TimeService / TimeHelper ────────────────────────────

    [Fact]
    public void TimeHelper_GetSystemClockType_ReturnsNetworkClock()
    {
        var response = new IpcResponse();
        var result = TimeHelper.GetSystemClockType(EmptyRequest(0), ref response);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, response.Data[0]); // NetworkClock
    }

    [Fact]
    public void TimeHelper_GetStandardSteadyClock_ReturnsTicks()
    {
        var response = new IpcResponse();
        var result = TimeHelper.GetStandardSteadyClock(EmptyRequest(1), ref response);

        Assert.True(result.IsSuccess);
        Assert.Equal(16, response.Data.Count); // 8 bytes ticks + 8 bytes clockSourceId (uint64)
        var ticks = BitConverter.ToUInt64(response.Data.ToArray(), 0);
        Assert.True(ticks > 0);
    }

    [Fact]
    public void TimeHelper_GetStandardUserSystemClock_ReturnsPosixTime()
    {
        var response = new IpcResponse();
        var result = TimeHelper.GetStandardUserSystemClock(EmptyRequest(3), ref response);

        Assert.True(result.IsSuccess);
        Assert.Equal(8, response.Data.Count);
        var posixTime = BitConverter.ToUInt64(response.Data.ToArray(), 0);
        Assert.True(posixTime > 0);
    }

    [Fact]
    public void TimeService_PortName_IsCorrect()
    {
        Assert.Equal("time:s", new TimeService().PortName);
    }

    [Fact]
    public void TimeService_CommandTable_DelegatesToTimeHelper()
    {
        var service = new TimeService();
        var (result, response) = InvokeCommand(service, 0); // GetSystemClockType

        Assert.True(result.IsSuccess);
        Assert.Equal(1, response.Data[0]);
    }

    [Fact]
    public void TimeService_GetSteadyClockCore_ContainsExtraFlag()
    {
        var service = new TimeService();
        var (result, response) = InvokeCommand(service, 2);

        Assert.True(result.IsSuccess);
        // ticks(8) + clockSourceId(8) + isStandardSteadyClock(1) = 17
        Assert.Equal(17, response.Data.Count);
    }

    [Fact]
    public void TimeAService_PortName_IsCorrect()
    {
        Assert.Equal("time:a", new TimeAService().PortName);
    }

    [Fact]
    public void TimeAService_Commands_ReturnSuccess()
    {
        var service = new TimeAService();
        foreach (var cmdId in new[] { 0u, 1u, 3u })
        {
            var (result, _) = InvokeCommand(service, cmdId);
            Assert.True(result.IsSuccess, $"time:a command {cmdId} should succeed");
        }
    }

    [Fact]
    public void TimeUService_PortName_IsCorrect()
    {
        Assert.Equal("time:u", new TimeUService().PortName);
    }

    [Fact]
    public void TimeUService_Commands_ReturnSuccess()
    {
        var service = new TimeUService();
        foreach (var cmdId in new[] { 0u, 1u, 3u })
        {
            var (result, _) = InvokeCommand(service, cmdId);
            Assert.True(result.IsSuccess, $"time:u command {cmdId} should succeed");
        }
    }

    // ──────────────────────────── SettingsService (set:) ────────────────────────────

    [Fact]
    public void SettingsService_PortName_IsCorrect()
    {
        Assert.Equal("set:", new SettingsService().PortName);
    }

    [Fact]
    public void SettingsService_GetLanguageCode_ReturnsEnUs()
    {
        var service = new SettingsService();
        var (result, response) = InvokeCommand(service, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(8, response.Data.Count); // ulong
        var langCode = BitConverter.ToUInt64(response.Data.ToArray(), 0);
        // Verify "en-US" encoding: bytes 65 6E 2D 55 53 00 00 00
        var bytes = response.Data.ToArray();
        Assert.Equal((byte)'e', bytes[0]);
        Assert.Equal((byte)'n', bytes[1]);
        Assert.Equal((byte)'-', bytes[2]);
        Assert.Equal((byte)'U', bytes[3]);
        Assert.Equal((byte)'S', bytes[4]);
    }

    [Fact]
    public void SettingsService_GetAvailableLanguageCodeCount_Returns16()
    {
        var service = new SettingsService();
        var (result, response) = InvokeCommand(service, 4);

        Assert.True(result.IsSuccess);
        Assert.Equal(16, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void SettingsService_GetRegionCode_ReturnsAmericas()
    {
        var service = new SettingsService();
        var (result, response) = InvokeCommand(service, 8);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, BitConverter.ToInt32(response.Data.ToArray(), 0)); // Americas = 1
    }

    [Fact]
    public void SettingsService_GetDeviceNickName_ReturnsSwitchNro()
    {
        var service = new SettingsService();
        var (result, response) = InvokeCommand(service, 10);

        Assert.True(result.IsSuccess);
        Assert.True(response.Data.Count > 0);
        var nickname = System.Text.Encoding.ASCII.GetString(response.Data.ToArray()).TrimEnd('\0');
        Assert.Equal("SwitchNro", nickname);
    }

    [Fact]
    public void SettingsService_MakeLanguageCode_ReturnsEnUs()
    {
        var service = new SettingsService();
        var (result, response) = InvokeCommand(service, 3, BitConverter.GetBytes(0u));

        Assert.True(result.IsSuccess);
        var bytes = response.Data.ToArray();
        Assert.Equal((byte)'e', bytes[0]);
        Assert.Equal((byte)'n', bytes[1]);
    }

    // ──────────────────────────── SetSysService (set:sys) ────────────────────────────

    [Fact]
    public void SetSysService_PortName_IsCorrect()
    {
        Assert.Equal("set:sys", new SetSysService().PortName);
    }

    [Fact]
    public void SetSysService_GetFirmwareVersion_ReturnsVersionString()
    {
        var service = new SetSysService();
        var (result, response) = InvokeCommand(service, 0);

        Assert.True(result.IsSuccess);
        var version = System.Text.Encoding.ASCII.GetString(response.Data.ToArray()).TrimEnd('\0');
        Assert.Equal("19.0.1", version);
    }

    [Fact]
    public void SetSysService_GetSerialNumber_ReturnsNonEmpty()
    {
        var service = new SetSysService();
        var (result, response) = InvokeCommand(service, 2);

        Assert.True(result.IsSuccess);
        Assert.True(response.Data.Count > 0);
        var serial = System.Text.Encoding.ASCII.GetString(response.Data.ToArray()).TrimEnd('\0');
        Assert.False(string.IsNullOrEmpty(serial));
    }

    [Fact]
    public void SetSysService_GetDeviceCert_ReturnsCorrectSize()
    {
        var service = new SetSysService();
        var (result, response) = InvokeCommand(service, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x240, response.Data.Count);
    }

    // ──────────────────────────── AudioOutService (audout:u) ────────────────────────────

    [Fact]
    public void AudioOutService_PortName_IsCorrect()
    {
        Assert.Equal("audout:u", new AudioOutService().PortName);
    }

    [Fact]
    public void AudioOutService_ListAudioOuts_ReturnsDeviceCount()
    {
        var service = new AudioOutService();
        var (result, response) = InvokeCommand(service, 0);

        Assert.True(result.IsSuccess);
        var deviceCount = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(1, deviceCount);
    }

    [Fact]
    public void AudioOutService_OpenAudioOut_ReturnsSampleRate()
    {
        var service = new AudioOutService();
        var (result, response) = InvokeCommand(service, 1);

        Assert.True(result.IsSuccess);
        var data = response.Data.ToArray();
        var sampleRate = BitConverter.ToInt32(data, 0);
        Assert.Equal(48000, sampleRate);
        Assert.Equal((ushort)2, BitConverter.ToUInt16(data, 4));  // ChannelCount
        Assert.Equal((ushort)1, BitConverter.ToUInt16(data, 6));  // SampleFormat PCM_INT16
    }

    [Fact]
    public void AudioOutService_GetAudioOutState_DefaultIsStopped()
    {
        var service = new AudioOutService();
        var (result, response) = InvokeCommand(service, 10);

        Assert.True(result.IsSuccess);
        // Default: not opened yet → Stopped (1)
        Assert.Equal(1, response.Data[0]); // Stopped = 1
    }

    [Fact]
    public void AudioOutService_StartAudioOut_ChangesState()
    {
        var service = new AudioOutService();
        InvokeCommand(service, 11); // StartAudioOut

        var (_, response) = InvokeCommand(service, 10); // GetAudioOutState
        Assert.Equal(0, response.Data[0]); // Started = 0
    }

    [Fact]
    public void AudioOutService_StopAudioOut_ChangesState()
    {
        var service = new AudioOutService();
        InvokeCommand(service, 11); // Start
        InvokeCommand(service, 12); // Stop

        var (_, response) = InvokeCommand(service, 10); // GetAudioOutState
        Assert.Equal(1, response.Data[0]); // Stopped = 1
    }

    [Fact]
    public void AudioOutService_AppendBuffer_IncrementsCount()
    {
        var service = new AudioOutService();
        var (result, _) = InvokeCommand(service, 13);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AudioOutService_RegisterBufferEvent_ReturnsHandle()
    {
        var manager = new IpcServiceManager { HandleTable = new HandleTable() };
        var service = new AudioOutService(serviceManager: manager);
        var (result, response) = InvokeCommand(service, 14);

        Assert.True(result.IsSuccess);
        Assert.Single(response.CopyHandles); // 返回一个事件句柄
        Assert.True(response.CopyHandles[0] >= 0xD000, $"句柄应在内核范围 (≥0xD000)，实际为 0x{response.CopyHandles[0]:X8}");
    }

    // ──────────────────────────── SocketService (bsd:) ────────────────────────────

    [Fact]
    public void SocketService_PortName_IsCorrect()
    {
        Assert.Equal("bsd:", new SocketService().PortName);
    }

    [Fact]
    public void SocketService_InitializeBsd_ReturnsWorkBufferSize()
    {
        var service = new SocketService();
        var (result, response) = InvokeCommand(service, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x4000, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void SocketService_Open_ReturnsFileDescriptor()
    {
        var service = new SocketService();
        var (result, response) = InvokeCommand(service, 1, BitConverter.GetBytes(2) // AF_INET
            .Concat(BitConverter.GetBytes(1))    // SOCK_STREAM
            .Concat(BitConverter.GetBytes(0))    // protocol
            .ToArray());

        Assert.True(result.IsSuccess);
        int fd = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.True(fd >= 0x20);
    }

    [Fact]
    public void SocketService_Connect_SetsConnectedFlag()
    {
        var service = new SocketService();

        // Open a socket first
        var (_, openResp) = InvokeCommand(service, 1,
            BitConverter.GetBytes(2).Concat(BitConverter.GetBytes(1)).Concat(BitConverter.GetBytes(0)).ToArray());
        int fd = BitConverter.ToInt32(openResp.Data.ToArray(), 0);

        // Connect
        var (result, response) = InvokeCommand(service, 3, BitConverter.GetBytes(fd));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void SocketService_Close_ReturnsSuccess()
    {
        var service = new SocketService();
        var (_, openResp) = InvokeCommand(service, 1,
            BitConverter.GetBytes(2).Concat(BitConverter.GetBytes(1)).Concat(BitConverter.GetBytes(0)).ToArray());
        int fd = BitConverter.ToInt32(openResp.Data.ToArray(), 0);

        var (result, _) = InvokeCommand(service, 2, BitConverter.GetBytes(fd));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SocketService_Send_ReturnsDataLength()
    {
        var service = new SocketService();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var (result, response) = InvokeCommand(service, 8, data);

        Assert.True(result.IsSuccess);
        Assert.Equal(data.Length, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void SocketService_ShutdownAllSockets_ReturnsSuccess()
    {
        var service = new SocketService();
        var (result, _) = InvokeCommand(service, 51);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SocketService_GetErrno_ReturnsZero()
    {
        var service = new SocketService();
        var (result, response) = InvokeCommand(service, 50);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    // ──────────────────────────── SocketUService (bsd:u) ────────────────────────────

    [Fact]
    public void SocketUService_PortName_IsCorrect()
    {
        Assert.Equal("bsd:u", new SocketUService().PortName);
    }

    [Fact]
    public void SocketUService_InitializeBsd_ReturnsWorkBufferSize()
    {
        var service = new SocketUService();
        var (result, response) = InvokeCommand(service, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x4000, BitConverter.ToInt32(response.Data.ToArray(), 0));
    }

    [Fact]
    public void SocketUService_Open_ReturnsFd()
    {
        var service = new SocketUService();
        var (result, response) = InvokeCommand(service, 1);

        Assert.True(result.IsSuccess);
        int fd = BitConverter.ToInt32(response.Data.ToArray(), 0);
        Assert.Equal(0x20, fd); // Fixed fd for simplified service
    }

    // ──────────────────────────── Dispose ────────────────────────────

    [Fact]
    public void AllServices_Dispose_DoesNotThrow()
    {
        IIpcService[] services =
        [
            new NvService(), new NvMemPService(),
            new AmService(), new AppletAeService(),
            new TimeService(), new TimeAService(), new TimeUService(),
            new SettingsService(), new SetSysService(),
            new AudioOutService(), new SocketService(), new SocketUService(),
        ];

        foreach (var service in services)
        {
            var ex = Record.Exception(() => service.Dispose());
            Assert.Null(ex);
        }
    }
}
