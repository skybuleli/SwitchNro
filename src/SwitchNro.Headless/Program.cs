using System;
using System.Collections.Generic;
using SwitchNro.Common;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using SwitchNro.Horizon;
using SwitchNro.Memory;
using SwitchNro.NroLoader;

namespace SwitchNro.Headless;

/// <summary>
/// 无头 NRO 运行器
/// 绕过 Avalonia UI，直接加载并运行 NRO 文件
/// 用于端到端验证 MVP 可行性
/// </summary>
internal sealed class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("用法: SwitchNro.Headless <nro文件路径> [最大SVC数]");
            Console.WriteLine("  nro文件路径: 要运行的 .nro 文件");
            Console.WriteLine("  最大SVC数:   SVC 分发循环上限 (默认 10000，防止无限循环)");
            Console.WriteLine("  -v:          启用详细日志 (Debug 级别)");
            return 1;
        }

        string nroPath = args[0];
        int maxSvcs = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 10000;

        // 设置日志级别：Debug 输出详细 SVC 处理信息，Info 输出关键事件
        bool verbose = args.Length > 2 && args[2] == "-v";
        Logger.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Info);

        Console.WriteLine($"═══════════════════════════════════════════");
        Console.WriteLine($"  SwitchNro 无头运行器");
        Console.WriteLine($"  NRO: {nroPath}");
        Console.WriteLine($"  最大 SVC 数: {maxSvcs}");
        Console.WriteLine($"  详细日志:   {(verbose ? "是" : "否")}");
        Console.WriteLine($"═══════════════════════════════════════════");

        try
        {
            RunNro(nroPath, maxSvcs);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"致命错误: {ex}");
            return 2;
        }
    }

    private static void RunNro(string nroPath, int maxSvcs)
    {
        // ── 1. 创建核心子系统 ──
        var memory = new VirtualMemoryManager();
        var svcDispatcher = new SvcDispatcher();
        var ipcServiceManager = new IpcServiceManager();
        var ipcBridge = new IpcBridge(ipcServiceManager, memory);
        var nroLoader = new NroLoader.NroLoader(memory);
        var horizonSystem = new HorizonSystem(memory, svcDispatcher);

        // ── 2. SmService.GetService 通过 HandleTable 创建真实的 KClientSession 句柄 ──
        // HandleTable 在 BindProcess 时动态设置（进程创建后才有句柄表）

        // ── 3. 注册核心 HLE 服务（与 MainWindow 一致）──
        Logger.SetMinimumLevel(LogLevel.Debug); // 强制开启 Debug 日志，看清 IPC 细节
        ipcServiceManager.RegisterService(new SmService(ipcServiceManager));
        ipcServiceManager.RegisterService(new FsService());
        ipcServiceManager.RegisterService(new ViService("vi:m"));
        ipcServiceManager.RegisterService(new ViService("vi:u"));
        ipcServiceManager.RegisterService(new ViService("vi:s"));
        ipcServiceManager.RegisterService(new HidService());
        ipcServiceManager.RegisterService(new NvService("nvdrv:a", ipcServiceManager));
        ipcServiceManager.RegisterService(new NvService("nvdrv:s", ipcServiceManager));
        ipcServiceManager.RegisterService(new NvService("nvdrv:t", ipcServiceManager));
        ipcServiceManager.RegisterService(new NvMemPService());
        ipcServiceManager.RegisterService(new AmService(ipcServiceManager));
        ipcServiceManager.RegisterService(new AppletAeService());
        ipcServiceManager.RegisterService(new TimeService());
        ipcServiceManager.RegisterService(new TimeUService());
        ipcServiceManager.RegisterService(new SettingsService());
        ipcServiceManager.RegisterService(new SetSysService());
        ipcServiceManager.RegisterService(new AudioOutService(ipcServiceManager));
        ipcServiceManager.RegisterService(new SocketService());
        ipcServiceManager.RegisterService(new SocketUService());
        ipcServiceManager.RegisterService(new PmDmntService(horizonSystem));
        ipcServiceManager.RegisterService(new PmInfoService(horizonSystem));
        ipcServiceManager.RegisterService(new PmShellService(horizonSystem));
        ipcServiceManager.RegisterService(new PmBmService());
        ipcServiceManager.RegisterService(new LdrShelService());
        ipcServiceManager.RegisterService(new LdrDmntService(horizonSystem));
        ipcServiceManager.RegisterService(new LdrPmService(horizonSystem));

        var lmLogger = new LmLoggerService();
        ipcServiceManager.RegisterService(new LmService(lmLogger));
        ipcServiceManager.RegisterService(new LmGetService(lmLogger));

        var arpRegistry = new ArpRegistry();
        ipcServiceManager.RegisterService(new ArpRService(arpRegistry));
        ipcServiceManager.RegisterService(new ArpWService(arpRegistry));

        ipcServiceManager.RegisterService(new SslService());

        var nifmGeneral = new NifmGeneralService();
        ipcServiceManager.RegisterService(new NifmUService(nifmGeneral));
        ipcServiceManager.RegisterService(new NifmSService(nifmGeneral));
        ipcServiceManager.RegisterService(new NifmAService(nifmGeneral));

        var accState = new AccountState();
        ipcServiceManager.RegisterService(new AccU0Service(accState));
        ipcServiceManager.RegisterService(new AccU1Service(accState));
        ipcServiceManager.RegisterService(new AccSuService(accState));

        var pctlState = new PctlState();
        ipcServiceManager.RegisterService(new PctlSService(pctlState));
        ipcServiceManager.RegisterService(new PctlRService(pctlState));
        ipcServiceManager.RegisterService(new PctlAService(pctlState));

        var fatalState = new FatalState();
        ipcServiceManager.RegisterService(new FatalUService(fatalState));
        ipcServiceManager.RegisterService(new FatalPService(fatalState));

        var apmState = new ApmState();
        ipcServiceManager.RegisterService(new ApmService(apmState));
        ipcServiceManager.RegisterService(new ApmPService(apmState));
        ipcServiceManager.RegisterService(new ApmSysService(apmState));

        var roState = new RoState();
        ipcServiceManager.RegisterService(new Ro1Service(roState));
        ipcServiceManager.RegisterService(new Ro1aService(roState));

        var audRenState = new AudRenState();
        ipcServiceManager.RegisterService(new AudRenUService(audRenState));
        ipcServiceManager.RegisterService(new AudRenU2Service(audRenState));

        var plState = new PlState();
        ipcServiceManager.RegisterService(new PlUService(plState));
        ipcServiceManager.RegisterService(new PlSService(plState));
        ipcServiceManager.RegisterService(new PlAService(plState));

        // ── 4. 注册 SVC 处理函数 ──
        RegisterCoreSvcs(svcDispatcher, horizonSystem, memory, ipcBridge);

        // ── 5. 加载 NRO 文件 ──
        Console.WriteLine($"\n▶ 加载 NRO: {nroPath}");
        var nroModule = nroLoader.Load(nroPath);
        Console.WriteLine($"  基地址: 0x{nroModule.BaseAddress:X16}");
        Console.WriteLine($"  入口点: 0x{nroModule.EntryPoint:X16}");
        Console.WriteLine($"  .text:  0x{nroModule.TextSegment.Address:X16} (0x{nroModule.Header.TextSize:X})");
        Console.WriteLine($"  .rodata: 0x{nroModule.RodataSegment.Address:X16} (0x{nroModule.Header.RodataSize:X})");
        Console.WriteLine($"  .data:  0x{nroModule.DataSegment.Address:X16} (0x{nroModule.Header.DataSize:X})");
        Console.WriteLine($"  .bss:   0x{nroModule.BssSegment.Address:X16} (0x{nroModule.Header.BssSize:X})");

        // ── 6. 创建进程并启动 ──
        var processInfo = new ProcessInfo
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(nroPath),
            EntryPoint = nroModule.EntryPoint,
            TitleId = 0x0100000000001000UL, // 提供一个真实的 TitleId
            Category = ProcessCategory.Application,
        };

        var process = horizonSystem.CreateProcess(nroModule, processInfo);
        horizonSystem.StartProcess(process);
        ipcBridge.BindProcess(process);

        // 强制同步 vCPU 寄存器状态到 HVF 并验证
        if (process.Engine is SwitchNro.Cpu.Hypervisor.HvfExecutionEngine hvfEngine)
        {
            hvfEngine.SetPC(processInfo.EntryPoint);
            hvfEngine.SetSP(0x0000000002100000UL); // 强制使用硬编码栈地址
            hvfEngine.SetPstate(0x0); // EL0h 模式

            ulong actualPC = hvfEngine.GetPC();
            ulong actualSP = hvfEngine.GetSP();
            Console.WriteLine($"  [HVF 校验] 同步后: PC=0x{actualPC:X16}, SP=0x{actualSP:X16}");
        }

        Console.WriteLine($"  PID: {processInfo.ProcessId}");
        Console.WriteLine($"  SP:  0x{process.Engine.GetSP():X16}");
        Console.WriteLine($"  TLS: 0x{process.TlsAddress:X16}");
        Console.WriteLine($"\n▶ 开始执行 (SVC 上限: {maxSvcs})...");

        // ── 7. 运行 SVC 分发循环（带超时保护）──
        int svcCount = 0;
        var engine = process.Engine;

        // 配置 HVF 引擎超时参数
        if (engine is SwitchNro.Cpu.Hypervisor.HvfExecutionEngine hvf)
        {
            hvf.VcpuTimeoutMs = 50;           // 每次 hv_vcpu_run() 最多 50ms
            hvf.MaxConsecutiveTimeouts = 100;  // 连续 100 次超时 (5秒) 后判定卡死
            Console.WriteLine($"  vCPU 超时: {hvf.VcpuTimeoutMs}ms，连续上限: {hvf.MaxConsecutiveTimeouts}");
        }
        Console.WriteLine();

        Console.WriteLine($"▶ 执行入口点: 0x{process.NroModule.EntryPoint:X16}");
        ExecutionResult result = ExecutionResult.NormalExit; // 默认值，供 goto Report 使用
        try
        {
            result = engine.Execute(process.NroModule.EntryPoint);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ 执行入口点异常: {ex.Message}");
            Console.Error.WriteLine($"   PC=0x{engine.GetPC():X16} SP=0x{engine.GetSP():X16}");
            goto Report;
        }

        // 报告首次执行结果
        Console.WriteLine($"  首次执行结果: {result}");

        while (result == ExecutionResult.SVC && svcCount < maxSvcs)
        {
            var svcInfo = engine.GetLastSvcInfo();
            var svcName = svcDispatcher.GetSvcName(svcInfo.SvcNumber);
            Console.WriteLine($"  [SVC #{svcCount}] 0x{svcInfo.SvcNumber:X2} ({svcName}) " +
                $"X0=0x{svcInfo.X0:X16} X1=0x{svcInfo.X1:X16} X2=0x{svcInfo.X2:X16} PC=0x{svcInfo.PC:X16}");

            SvcResult svcResult;
            try
            {
                svcResult = svcDispatcher.Dispatch(svcInfo);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    ❌ SVC 处理异常: {ex.Message}");
                svcResult = new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.NotImplemented) };
            }

            // 将结果写回 vCPU 寄存器
            ulong x0 = svcResult.ReturnCode.IsSuccess ? 0UL : unchecked((ulong)svcResult.ReturnCode.Value);
            engine.SetSvcResult(x0, svcResult.ReturnValue1, svcResult.ReturnValue2);

            if (!svcResult.ReturnCode.IsSuccess)
            {
                Console.WriteLine($"    → 错误: Module={svcResult.ReturnCode.Module} " +
                    $"Desc={svcResult.ReturnCode.Description} (0x{svcResult.ReturnCode.Value:X8})");
            }
            else if (svcResult.ReturnValue1 != 0 || svcResult.ReturnValue2 != 0)
            {
                Console.WriteLine($"    → 成功: X1=0x{svcResult.ReturnValue1:X16} X2=0x{svcResult.ReturnValue2:X16}");
            }

 svcCount++;

 // 检查 ExitProcess 后进程是否已退出
 if (process.State == ProcessState.Exited)
 {
 result = ExecutionResult.ProcessExited;
 Console.WriteLine($" → 进程已退出，停止执行");
 break;
 }

 try
 {
 result = engine.RunNext();
 }
 catch (Exception ex)
 {
 Console.Error.WriteLine($"❌ RunNext 异常: {ex.Message}");
 Console.Error.WriteLine($" PC=0x{engine.GetPC():X16} SP=0x{engine.GetSP():X16}");
 break;
 }

 // 报告非 SVC 结果
 if (result != ExecutionResult.SVC)
 {
 Console.WriteLine($" 执行结果变更: {result} (PC=0x{engine.GetPC():X16})");
 if (result == ExecutionResult.Timeout)
 break; // 超时退出循环
 }
 }

 Report:
 // ── 8. 报告结果 ──
 Console.WriteLine($"\n═══════════════════════════════════════════");
 Console.WriteLine($" 执行结束");
 Console.WriteLine($" SVC 调用数: {svcCount}");
 Console.WriteLine($" 退出原因: {result}");
 Console.WriteLine($" 进程状态: {process.State}");
 Console.WriteLine($" 最终 PC: 0x{engine.GetPC():X16}");
 Console.WriteLine($" 最终 SP: 0x{engine.GetSP():X16}");
 if (result == ExecutionResult.MemoryFault)
 Console.WriteLine($" ⚠️ 内存异常! 可能是未映射的区域或权限错误");
 if (result == ExecutionResult.UndefinedInstruction)
 Console.WriteLine($" ⚠️ 未定义指令! 可能是 CPU 不支持的指令");
 if (result == ExecutionResult.ProcessExited)
 Console.WriteLine($" ✓ 程序已完成并干净退出");
 if (svcCount >= maxSvcs)
 Console.WriteLine($" ⚠️ 已达 SVC 上限，可能存在无限循环");
 if (result == ExecutionResult.Timeout)
 Console.WriteLine($" ⚠️ vCPU 执行超时! Guest 代码可能陷入无 SVC 的死循环");

        // 打印 vCPU 退出统计
        if (engine is SwitchNro.Cpu.Hypervisor.HvfExecutionEngine hvfStats)
            hvfStats.PrintExitStatistics();

        Console.WriteLine($"═══════════════════════════════════════════");

        // 清理
        horizonSystem.Dispose();
        memory.Dispose();
    }

    private static void RegisterCoreSvcs(
        SvcDispatcher dispatcher,
        HorizonSystem system,
        VirtualMemoryManager memory,
        IpcBridge ipcBridge)
    {
        // SVC 0x26: OutputDebugString
        dispatcher.Register(0x26, svc =>
        {
            // 尝试两种可能的寄存器约定 (X0=str, X1=len 或 X1=str, X2=len)
            ulong addr = svc.X0 != 0 ? svc.X0 : svc.X1;
            ulong len  = svc.X0 != 0 ? svc.X1 : svc.X2;
            
            if (addr != 0 && len > 0 && len < 4096)
            {
                try
                {
                    var buf = new byte[len];
                    memory.Read(addr, buf);
                    var msg = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
                    Console.WriteLine($"    [DebugString] {msg}");
                }
                catch { /* 忽略 */ }
            }
            return new SvcResult { ReturnCode = ResultCode.Success };
        });

        dispatcher.Register(0x01, svc => system.SetHeapSize(svc));
        dispatcher.Register(0x05, svc => system.QueryMemory(svc));
        dispatcher.Register(0x06, svc => system.ExitProcess(svc));
        dispatcher.Register(0x07, svc => system.ExitThread(svc));
        dispatcher.Register(0x08, svc => system.SleepThread(svc));
        dispatcher.Register(0x0D, svc => system.WaitSynchronization(svc));
        dispatcher.Register(0x0E, svc => system.CancelSynchronization(svc));
        dispatcher.Register(0x0F, svc => system.ArbitrateLock(svc));
        dispatcher.Register(0x10, svc => system.ArbitrateUnlock(svc));
        dispatcher.Register(0x11, svc => system.WaitProcessWideKeyAtomic(svc));
        dispatcher.Register(0x12, svc => system.SignalProcessWideKey(svc));
        dispatcher.Register(0x03, svc => system.MapMemory(svc));
        dispatcher.Register(0x04, svc => system.UnmapMemory(svc));
        dispatcher.Register(0x09, svc => system.GetThreadPriority(svc));
        dispatcher.Register(0x0A, svc => system.SetThreadPriority(svc));
        dispatcher.Register(0x34, svc => system.CreateThread(svc));
        dispatcher.Register(0x40, svc => system.SetThreadActivity(svc));
        dispatcher.Register(0x4C, svc => system.StartThread(svc));
        dispatcher.Register(0x35, svc => system.MapPhysicalMemory(svc));
        dispatcher.Register(0x36, svc => system.UnmapPhysicalMemory(svc));
        dispatcher.Register(0x0C, _ => new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 0 });
        dispatcher.Register(0x13, _ =>
        {
            var tick = (ulong)System.Diagnostics.Stopwatch.GetTimestamp();
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = tick };
        });
        dispatcher.Register(0x32, svc => {
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 0x100 }; // 返回 PID 0x100
        });
        
        dispatcher.Register(0x33, svc => {
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 0x200 }; // 返回 TID 0x200
        });

        dispatcher.Register(0x28, svc => system.GetInfo(svc));
        dispatcher.Register(0x29, svc => system.GetInfo(svc));
        dispatcher.Register(0x19, svc => system.CloseHandle(svc));
        // SVC 0x1F: ConnectToNamedPort
        dispatcher.Register(0x1F, svc => {
            string port = "";
            try {
                var buf = new byte[8];
                memory.Read(svc.X0, buf);
                port = System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0');
            } catch {}
            Console.WriteLine($"    [ConnectToNamedPort] port=\"{port}\"");
            return ipcBridge.ConnectToNamedPort(svc);
        });
        dispatcher.Register(0x21, svc => ipcBridge.SendSyncRequest(svc));
        dispatcher.Register(0x22, svc => ipcBridge.SendSyncRequestWithUserBuffer(svc));
        dispatcher.Register(0x43, svc => ipcBridge.ReplyAndReceive(svc));
        dispatcher.Register(0x44, svc => ipcBridge.ReplyAndReceiveWithUserBuffer(svc));

        // SVC 0x42: WaitForAddress (homebrew futex)
        dispatcher.Register(0x42, svc =>
        {
            // 简化实现：直接返回 Success（非阻塞）
            Console.WriteLine($"    [WaitForAddress] addr=0x{svc.X0:X16} (stub: 返回 Success)");
            return new SvcResult { ReturnCode = ResultCode.Success };
        });

        // SVC 0x45: SignalToAddress
        dispatcher.Register(0x45, svc =>
        {
            Console.WriteLine($"    [SignalToAddress] addr=0x{svc.X0:X16} (stub: 返回 Success)");
            return new SvcResult { ReturnCode = ResultCode.Success };
        });

        // SVC 0x14: CreateSharedMemory
        // 用户态分配共享内存
        dispatcher.Register(0x14, svc => system.CreateSharedMemory(svc));

        // SVC 0x15: MapSharedMemory
        // 将共享内存映射到当前进程地址空间
        dispatcher.Register(0x15, svc => system.MapSharedMemory(svc));

        // SVC 0x16: UnmapSharedMemory
        // 取消共享内存映射
        dispatcher.Register(0x16, svc => system.UnmapSharedMemory(svc));

        // SvcDispatcher 对未注册的 SVC 自动返回 NotImplemented，无需额外设置
    }
}
