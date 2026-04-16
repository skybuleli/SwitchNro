using System;
using System.Text;
using SwitchNro.Common;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using Xunit;

using static SwitchNro.Tests.IpcTestHelper;

namespace SwitchNro.Tests;

/// <summary>
/// 日志管理服务单元测试 — LmService, LmLoggerService, LmGetService
/// </summary>
public class LmServiceTests
{
    /// <summary>创建共享的 Logger + LmService 实例</summary>
    private static (LmLoggerService Logger, LmService Service) CreateLmService()
    {
        var logger = new LmLoggerService();
        var service = new LmService(logger);
        return (logger, service);
    }

    // ──────────────────────────── LmService (lm) ────────────────────────────

    [Fact]
    public void LmService_PortName_是lm()
    {
        var (_, service) = CreateLmService();
        Assert.Equal("lm", service.PortName);
    }

    [Fact]
    public void LmService_命令表包含0()
    {
        var (_, service) = CreateLmService();
        Assert.True(service.CommandTable.ContainsKey(0)); // OpenLogger
        Assert.Single(service.CommandTable);
    }

    [Fact]
    public void LmService_未知命令返回错误()
    {
        var (_, service) = CreateLmService();
        var (result, _) = InvokeCommand(service, 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LmService_OpenLogger_返回句柄()
    {
        var (_, service) = CreateLmService();
        var pid = BitConverter.GetBytes(0x42UL);
        var (result, response) = InvokeCommand(service, 0, pid);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count); // int32 handle
    }

    [Fact]
    public void LmService_OpenLogger_空数据也返回成功()
    {
        var (_, service) = CreateLmService();
        var (result, response) = InvokeCommand(service, 0);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, response.Data.Count);
    }

    [Fact]
    public void LmService_OpenLogger_多次调用返回递增句柄()
    {
        var (_, service) = CreateLmService();
        var pid = BitConverter.GetBytes(0x42UL);

        var (_, r1) = InvokeCommand(service, 0, pid);
        var (_, r2) = InvokeCommand(service, 0, pid);

        int h1 = BitConverter.ToInt32(r1.Data.ToArray(), 0);
        int h2 = BitConverter.ToInt32(r2.Data.ToArray(), 0);
        Assert.Equal(h1 + 1, h2); // _nextLoggerHandle 递增
    }

    [Fact]
    public void LmService_GetLogger_有效句柄返回Logger()
    {
        var (logger, service) = CreateLmService();
        var pid = BitConverter.GetBytes(0x42UL);
        var (_, resp) = InvokeCommand(service, 0, pid);
        int handle = BitConverter.ToInt32(resp.Data.ToArray(), 0);

        var found = service.GetLogger(handle);
        Assert.NotNull(found);
        Assert.Same(logger, found);
    }

    [Fact]
    public void LmService_GetLogger_无效句柄返回null()
    {
        var (_, service) = CreateLmService();
        Assert.Null(service.GetLogger(unchecked((int)0xDEADBEEF)));
    }

    // ──────────────────────────── LmLoggerService (ILogger) ────────────────────────────

    [Fact]
    public void LmLoggerService_PortName_是ilm()
    {
        Assert.Equal("ilm", new LmLoggerService().PortName);
    }

    [Fact]
    public void LmLoggerService_命令表包含0和1和2()
    {
        var service = new LmLoggerService();
        Assert.True(service.CommandTable.ContainsKey(0)); // Log
        Assert.True(service.CommandTable.ContainsKey(1)); // SetDestination
        Assert.True(service.CommandTable.ContainsKey(2)); // TransmitHashedLog
        Assert.Equal(3, service.CommandTable.Count);
    }

    [Fact]
    public void LmLoggerService_未知命令返回错误()
    {
        var (result, _) = InvokeCommand(new LmLoggerService(), 99);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LmLoggerService_Log_写入日志消息()
    {
        var logger = new LmLoggerService();
        var msg = Encoding.UTF8.GetBytes("Hello from guest\0");
        var (result, _) = InvokeCommand(logger, 0, msg);
        Assert.True(result.IsSuccess);
        Assert.Single(logger.LogBuffer);
        Assert.Equal("Hello from guest", logger.LogBuffer[0]);
    }

    [Fact]
    public void LmLoggerService_Log_总是返回成功()
    {
        // ILogger.Log per SwitchBrew: always returns success
        var logger = new LmLoggerService();
        var (result, _) = InvokeCommand(logger, 0);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void LmLoggerService_Log_空数据不添加条目()
    {
        var logger = new LmLoggerService();
        var (result, _) = InvokeCommand(logger, 0);
        Assert.True(result.IsSuccess);
        Assert.Empty(logger.LogBuffer);
    }

    [Fact]
    public void LmLoggerService_Log_多条消息按序存储()
    {
        var logger = new LmLoggerService();
        InvokeCommand(logger, 0, Encoding.UTF8.GetBytes("first\0"));
        InvokeCommand(logger, 0, Encoding.UTF8.GetBytes("second\0"));
        InvokeCommand(logger, 0, Encoding.UTF8.GetBytes("third\0"));

        Assert.Equal(3, logger.LogBuffer.Count);
        Assert.Equal("first", logger.LogBuffer[0]);
        Assert.Equal("second", logger.LogBuffer[1]);
        Assert.Equal("third", logger.LogBuffer[2]);
    }

    [Fact]
    public void LmLoggerService_Log_缓冲区满时淘汰旧条目并递增丢弃计数()
    {
        var logger = new LmLoggerService();

        // 填满缓冲区 (MaxLogEntries = 256)
        for (int i = 0; i < 256; i++)
            InvokeCommand(logger, 0, Encoding.UTF8.GetBytes($"msg{i}\0"));

        Assert.Equal(256, logger.LogBuffer.Count);
        Assert.Equal(0UL, logger.DropCount);

        // 再加一条，触发淘汰
        InvokeCommand(logger, 0, Encoding.UTF8.GetBytes("overflow\0"));
        Assert.Equal(256, logger.LogBuffer.Count);
        Assert.Equal(1UL, logger.DropCount);
        Assert.Equal("msg1", logger.LogBuffer[0]); // msg0 被淘汰
        Assert.Equal("overflow", logger.LogBuffer[255]);
    }

    [Fact]
    public void LmLoggerService_SetDestination_默认0xFFFF()
    {
        var logger = new LmLoggerService();
        Assert.Equal(0xFFFFU, logger.DestinationMask);
    }

    [Fact]
    public void LmLoggerService_SetDestination_设置新掩码()
    {
        var logger = new LmLoggerService();
        var mask = BitConverter.GetBytes(0x02U); // UART only
        var (result, _) = InvokeCommand(logger, 1, mask);
        Assert.True(result.IsSuccess);
        Assert.Equal(0x02U, logger.DestinationMask);
    }

    [Fact]
    public void LmLoggerService_SetDestination_空数据返回LmError()
    {
        var (result, _) = InvokeCommand(new LmLoggerService(), 1);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultCode.LmResult(2), result);
    }

    [Fact]
    public void LmLoggerService_TransmitHashedLog_返回成功()
    {
        var logger = new LmLoggerService();
        var (result, response) = InvokeCommand(logger, 2);
        Assert.True(result.IsSuccess);
        Assert.Empty(response.Data);
    }

    [Fact]
    public void LmLoggerService_IsLogging_默认false()
    {
        var logger = new LmLoggerService();
        Assert.False(logger.IsLogging);
    }

    [Fact]
    public void LmLoggerService_IsLogging_可外部设置()
    {
        var logger = new LmLoggerService();
        logger.IsLogging = true;
        Assert.True(logger.IsLogging);
    }

    // ──────────────────────────── LmGetService (lm:get) ────────────────────────────

    [Fact]
    public void LmGetService_PortName_是lmGet()
    {
        var logger = new LmLoggerService();
        Assert.Equal("lm:get", new LmGetService(logger).PortName);
    }

    [Fact]
    public void LmGetService_命令表包含0和1和2()
    {
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);
        Assert.True(service.CommandTable.ContainsKey(0)); // StartLogging
        Assert.True(service.CommandTable.ContainsKey(1)); // StopLogging
        Assert.True(service.CommandTable.ContainsKey(2)); // GetLog
        Assert.Equal(3, service.CommandTable.Count);
    }

    [Fact]
    public void LmGetService_StartLogging_设置IsLogging为true()
    {
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);
        Assert.False(logger.IsLogging);

        var (result, _) = InvokeCommand(service, 0);
        Assert.True(result.IsSuccess);
        Assert.True(logger.IsLogging);
    }

    [Fact]
    public void LmGetService_StopLogging_设置IsLogging为false()
    {
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);
        logger.IsLogging = true;

        var (result, _) = InvokeCommand(service, 1);
        Assert.True(result.IsSuccess);
        Assert.False(logger.IsLogging);
    }

    [Fact]
    public void LmGetService_GetLog_未启动日志返回空数据()
    {
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);
        // IsLogging = false, 无日志

        var (result, response) = InvokeCommand(service, 2);
        Assert.True(result.IsSuccess);

        // 末尾有 readSize(8) + dropCount(8) = 16 bytes
        Assert.True(response.Data.Count >= 16);
        ulong readSize = BitConverter.ToUInt64(response.Data.ToArray(), response.Data.Count - 16);
        Assert.Equal(0UL, readSize);
    }

    [Fact]
    public void LmGetService_GetLog_启动后有日志返回数据()
    {
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);

        // 写入日志
        InvokeCommand(logger, 0, Encoding.UTF8.GetBytes("test log\0"));
        logger.IsLogging = true;

        var (result, response) = InvokeCommand(service, 2);
        Assert.True(result.IsSuccess);

        // 数据应包含日志文本 + readSize(8) + dropCount(8)
        Assert.True(response.Data.Count > 16);
        ulong dropCount = BitConverter.ToUInt64(response.Data.ToArray(), response.Data.Count - 8);
        Assert.Equal(0UL, dropCount);
    }

    [Fact]
    public void LmGetService_GetLog_缓冲区溢出后DropCount非零()
    {
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);

        // 填满缓冲区并触发淘汰
        for (int i = 0; i <= 256; i++)
            InvokeCommand(logger, 0, Encoding.UTF8.GetBytes($"msg{i}\0"));
        logger.IsLogging = true;

        var (result, response) = InvokeCommand(service, 2);
        Assert.True(result.IsSuccess);
        Assert.True(response.Data.Count > 16);
        ulong dropCount = BitConverter.ToUInt64(response.Data.ToArray(), response.Data.Count - 8);
        Assert.Equal(1UL, dropCount); // 1 条被淘汰
    }

    [Fact]
    public void LmGetService_StartStopLogging_共享同一Logger()
    {
        var logger = new LmLoggerService();
        var lmService = new LmService(logger);
        var lmGetService = new LmGetService(logger);

        // 通过 lm:get 启动日志
        InvokeCommand(lmGetService, 0);
        Assert.True(logger.IsLogging);

        // 通过 lm:get 停止日志
        InvokeCommand(lmGetService, 1);
        Assert.False(logger.IsLogging);
    }

    // ──────────────────────────── IpcServiceManager 集成 ────────────────────────────

    [Fact]
    public void IpcServiceManager_LmService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var logger = new LmLoggerService();
        var service = new LmService(logger);
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("lm"));
    }

    [Fact]
    public void IpcServiceManager_LmGetService_可注册和查询()
    {
        var manager = new IpcServiceManager();
        var logger = new LmLoggerService();
        var service = new LmGetService(logger);
        manager.RegisterService(service);
        Assert.Same(service, manager.GetService("lm:get"));
    }

    // ──────────────────────────── IDisposable ────────────────────────────

    [Fact]
    public void LmService_Dispose_不抛异常()
    {
        var (_, service) = CreateLmService();
        service.Dispose();
    }

    [Fact]
    public void LmLoggerService_Dispose_不抛异常()
    {
        new LmLoggerService().Dispose();
    }

    [Fact]
    public void LmGetService_Dispose_不抛异常()
    {
        new LmGetService(new LmLoggerService()).Dispose();
    }
}
