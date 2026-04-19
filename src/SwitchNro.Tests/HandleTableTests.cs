using SwitchNro.Horizon;
using Xunit;

namespace SwitchNro.Tests;

public class HandleTableTests
{
    [Fact]
    public void CreateHandle_ReturnsIncrementingIds()
    {
        var table = new HandleTable();
        var h1 = table.CreateHandle(new KClientSession("sm:"));
        var h2 = table.CreateHandle(new KClientSession("fs:"));
        var h3 = table.CreateHandle(new KClientSession("hid:"));

        Assert.Equal(0xD000, h1);
        Assert.Equal(0xD001, h2);
        Assert.Equal(0xD002, h3);
        Assert.Equal(3, table.Count);
    }

    [Fact]
    public void GetObject_ValidHandle_ReturnsObject()
    {
        var table = new HandleTable();
        var session = new KClientSession("sm:");
        var handle = table.CreateHandle(session);

        var obj = table.GetObject(handle);
        Assert.NotNull(obj);
        Assert.IsType<KClientSession>(obj);

        var typed = table.GetObject<KClientSession>(handle);
        Assert.NotNull(typed);
        Assert.Equal("sm:", typed!.ServicePortName);
    }

    [Fact]
    public void GetObject_InvalidHandle_ReturnsNull()
    {
        var table = new HandleTable();
        Assert.Null(table.GetObject(0xDEAD));
        Assert.Null(table.GetObject<KClientSession>(0xDEAD));
    }

    [Fact]
    public void GetObject_WrongType_ReturnsNull()
    {
        var table = new HandleTable();
        var session = new KClientSession("sm:");
        var handle = table.CreateHandle(session);

        // KClientSession 不能转换为 KEvent
        Assert.Null(table.GetObject<KEvent>(handle));
    }

    [Fact]
    public void CloseHandle_ValidHandle_Succeeds()
    {
        var table = new HandleTable();
        var handle = table.CreateHandle(new KClientSession("sm:"));

        Assert.True(table.CloseHandle(handle));
        Assert.Equal(0, table.Count);
        Assert.Null(table.GetObject(handle));
    }

    [Fact]
    public void CloseHandle_InvalidHandle_ReturnsFalse()
    {
        var table = new HandleTable();
        Assert.False(table.CloseHandle(0xDEAD));
    }

    [Fact]
    public void CloseHandle_DoubleClose_ReturnsFalse()
    {
        var table = new HandleTable();
        var handle = table.CreateHandle(new KClientSession("sm:"));

        Assert.True(table.CloseHandle(handle));
        Assert.False(table.CloseHandle(handle));
    }

    [Fact]
    public void IsValid_RecognizesValidAndInvalid()
    {
        var table = new HandleTable();
        var handle = table.CreateHandle(new KEvent());

        Assert.True(table.IsValid(handle));
        Assert.False(table.IsValid(0xDEAD));
    }

    [Fact]
    public void KClientSession_HasCorrectProperties()
    {
        var session = new KClientSession("fs:");
        Assert.Equal("fs:", session.ServicePortName);
        Assert.Equal("ClientSession", session.ObjectType);
    }

    [Fact]
    public void KEvent_DefaultNotSignaled()
    {
        var evt = new KEvent();
        Assert.False(evt.IsSignaled);
        Assert.Equal("KEvent", evt.ObjectType);
    }

    [Fact]
    public void KEvent_CanSetSignaled()
    {
        var evt = new KEvent(true);
        Assert.True(evt.IsSignaled);

        evt.IsSignaled = false;
        Assert.False(evt.IsSignaled);
    }

    [Fact]
    public void KReadableEvent_DefaultNotSignaled()
    {
        var evt = new KReadableEvent();
        Assert.False(evt.IsSignaled);
        Assert.Equal("ReadableEvent", evt.ObjectType);
    }

    [Fact]
    public void Clear_RemovesAllHandles()
    {
        var table = new HandleTable();
        table.CreateHandle(new KClientSession("sm:"));
        table.CreateHandle(new KClientSession("fs:"));
        Assert.Equal(2, table.Count);

        table.Clear();
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void MultipleHandleTypes_Coexist()
    {
        var table = new HandleTable();
        var h1 = table.CreateHandle(new KClientSession("sm:"));
        var h2 = table.CreateHandle(new KEvent(true));
        var h3 = table.CreateHandle(new KReadableEvent());

        Assert.IsType<KClientSession>(table.GetObject(h1));
        Assert.IsType<KEvent>(table.GetObject(h2));
        Assert.IsType<KReadableEvent>(table.GetObject(h3));

        Assert.True(table.GetObject<KEvent>(h2)!.IsSignaled);
        Assert.False(table.GetObject<KReadableEvent>(h3)!.IsSignaled);
    }
}
