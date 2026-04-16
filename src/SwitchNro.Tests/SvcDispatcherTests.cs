using SwitchNro.Cpu;
using SwitchNro.Common;
using Xunit;

namespace SwitchNro.Tests;

public class SvcDispatcherTests
{
    [Fact]
    public void Dispatch_RegisteredHandler_ReturnsExpectedResult()
    {
        var dispatcher = new SvcDispatcher();
        dispatcher.Register(0x01, svc => new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 0x1000 });

        var svcInfo = new SvcInfo { SvcNumber = 0x01 };
        var result = dispatcher.Dispatch(svcInfo);

        Assert.True(result.ReturnCode.IsSuccess);
        Assert.Equal(0x1000UL, result.ReturnValue1);
    }

    [Fact]
    public void Dispatch_UnregisteredSvc_ReturnsNotImplemented()
    {
        var dispatcher = new SvcDispatcher();
        var svcInfo = new SvcInfo { SvcNumber = 0xFF };
        var result = dispatcher.Dispatch(svcInfo);

        Assert.False(result.ReturnCode.IsSuccess);
        Assert.Equal(1, result.ReturnCode.Module); // Kernel module
    }

    [Fact]
    public void GetSvcName_KnownSvc_ReturnsCorrectName()
    {
        var dispatcher = new SvcDispatcher();
        var name = dispatcher.GetSvcName(0x26);

        Assert.Equal("OutputDebugString", name);
    }

    [Fact]
    public void GetSvcName_UnknownSvc_ReturnsUnknownFormat()
    {
        var dispatcher = new SvcDispatcher();
        var name = dispatcher.GetSvcName(0xFE);

        Assert.Contains("Unknown", name);
    }
}
