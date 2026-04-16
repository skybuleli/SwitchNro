using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SwitchNro.Audio;
using SwitchNro.Audio.SDL2;
using SwitchNro.Common;
using SwitchNro.Common.Configuration;
using SwitchNro.Common.Logging;
using SwitchNro.Cpu;
using SwitchNro.Debugger;
using SwitchNro.HLE.Ipc;
using SwitchNro.HLE.Services;
using SwitchNro.Horizon;
using SwitchNro.Input;
using SwitchNro.Memory;
using SwitchNro.NroLoader;
using SwitchNro.Graphics.GAL;
using SwitchNro.Graphics.Metal;

namespace SwitchNro.UI;

public partial class MainWindow : Window, IDisposable
{
    // 核心子系统
    private VirtualMemoryManager? _memory;
    private SvcDispatcher? _svcDispatcher;
    private HorizonSystem? _horizonSystem;
    private NroLoader.NroLoader? _nroLoader;
    private InputManager? _inputManager;
    private DebuggerService? _debuggerService;
    private IpcServiceManager? _ipcServiceManager;
    private Sdl2AudioBackend? _audioBackend;
    private MetalRenderer? _renderer;

    // UI 数据绑定
    private readonly ObservableCollection<string> _svcLogEntries = new();
    private readonly ObservableCollection<string> _serviceEntries = new();

    private EmulatorConfig? _config;

    public MainWindow()
    {
        InitializeComponent();
        InitializeSubsystem();
        SetupUiEvents();
    }

    private void InitializeSubsystem()
    {
        // 加载配置
        _config = ConfigManager.Load();

        // 创建核心子系统
        _memory = new VirtualMemoryManager();
        _svcDispatcher = new SvcDispatcher();
        _nroLoader = new NroLoader.NroLoader(_memory);
        _inputManager = new InputManager();
        _debuggerService = new DebuggerService();
        _ipcServiceManager = new IpcServiceManager();

        // 注册核心 HLE 服务
        _ipcServiceManager.RegisterService(new SmService(_ipcServiceManager));
        _ipcServiceManager.RegisterService(new FsService());
        _ipcServiceManager.RegisterService(new ViService());
        _ipcServiceManager.RegisterService(new HidService());

        // 新增 HLE 服务
        _ipcServiceManager.RegisterService(new NvService());
        _ipcServiceManager.RegisterService(new NvMemPService());
        _ipcServiceManager.RegisterService(new AmService());
        _ipcServiceManager.RegisterService(new AppletAeService());
        _ipcServiceManager.RegisterService(new TimeService());
        _ipcServiceManager.RegisterService(new TimeUService());
        _ipcServiceManager.RegisterService(new SettingsService());
        _ipcServiceManager.RegisterService(new SetSysService());
        _ipcServiceManager.RegisterService(new AudioOutService());
        _ipcServiceManager.RegisterService(new SocketService());
        _ipcServiceManager.RegisterService(new SocketUService());

        // 创建音频后端
        _audioBackend = new Sdl2AudioBackend();
        _audioBackend.Initialize(new AudioBackendConfig());

        // 创建图形渲染器
        _renderer = new MetalRenderer();
        _renderer.Initialize(new RendererCreateInfo());

        // 创建 Horizon 系统
        _horizonSystem = new HorizonSystem(_memory, _svcDispatcher);

        // 进程管理服务（需要在 _horizonSystem 创建之后注册）
        _ipcServiceManager.RegisterService(new PmDmntService(_horizonSystem));
        _ipcServiceManager.RegisterService(new PmInfoService(_horizonSystem));
        _ipcServiceManager.RegisterService(new PmShellService(_horizonSystem));
        _ipcServiceManager.RegisterService(new PmBmService());

        // 加载器服务
        _ipcServiceManager.RegisterService(new LdrShelService());
        _ipcServiceManager.RegisterService(new LdrDmntService(_horizonSystem));
        _ipcServiceManager.RegisterService(new LdrPmService(_horizonSystem));

        // 日志管理服务
        var lmLogger = new LmLoggerService();
        _ipcServiceManager.RegisterService(new LmService(lmLogger));
        _ipcServiceManager.RegisterService(new LmGetService(lmLogger));

        // ARP Glue 服务
        var arpRegistry = new ArpRegistry();
        _ipcServiceManager.RegisterService(new ArpRService(arpRegistry));
        _ipcServiceManager.RegisterService(new ArpWService(arpRegistry));

        // SSL 服务
        _ipcServiceManager.RegisterService(new SslService());

        // 网络接口管理服务 (共享 NifmGeneralService)
        var nifmGeneral = new NifmGeneralService();
        _ipcServiceManager.RegisterService(new NifmUService(nifmGeneral));
        _ipcServiceManager.RegisterService(new NifmSService(nifmGeneral));
        _ipcServiceManager.RegisterService(new NifmAService(nifmGeneral));

        // 账户服务 (共享 AccountState)
        var accState = new AccountState();
        _ipcServiceManager.RegisterService(new AccU0Service(accState));
        _ipcServiceManager.RegisterService(new AccU1Service(accState));
        _ipcServiceManager.RegisterService(new AccSuService(accState));

        // 家长控制服务 (共享 PctlState)
        var pctlState = new PctlState();
        _ipcServiceManager.RegisterService(new PctlSService(pctlState));
        _ipcServiceManager.RegisterService(new PctlRService(pctlState));
        _ipcServiceManager.RegisterService(new PctlAService(pctlState));

        // 好友服务 (共享 FriendState)
        var friendState = new FriendState();
        _ipcServiceManager.RegisterService(new FriendUService(friendState));
        _ipcServiceManager.RegisterService(new FriendVService(friendState));
        _ipcServiceManager.RegisterService(new FriendMService(friendState));
        _ipcServiceManager.RegisterService(new FriendSService(friendState));
        _ipcServiceManager.RegisterService(new FriendAService(friendState));

        // 应用管理服务 (共享 NsAppManagerState)
        var nsState = new NsAppManagerState();
        _ipcServiceManager.RegisterService(new NsAm2Service(nsState));
        _ipcServiceManager.RegisterService(new NsAmService(nsState));
        _ipcServiceManager.RegisterService(new NsAeService(nsState));
        _ipcServiceManager.RegisterService(new NsSuService());
        _ipcServiceManager.RegisterService(new NsDevService());

        // BCAT 服务 (共享 BcatState)
        var bcatState = new BcatState();
        _ipcServiceManager.RegisterService(new BcatAService(bcatState));
        _ipcServiceManager.RegisterService(new BcatMService(bcatState));
        _ipcServiceManager.RegisterService(new BcatUService(bcatState));
        _ipcServiceManager.RegisterService(new BcatSService(bcatState));

        // News 服务 (共享 NewsState)
        var newsState = new NewsState();
        _ipcServiceManager.RegisterService(new NewsAService(newsState));
        _ipcServiceManager.RegisterService(new NewsCService(newsState));
        _ipcServiceManager.RegisterService(new NewsMService(newsState));
        _ipcServiceManager.RegisterService(new NewsPService(newsState));
        _ipcServiceManager.RegisterService(new NewsVService(newsState));

        // MM 内存监控服务 (共享 MmState)
        var mmState = new MmState();
        _ipcServiceManager.RegisterService(new MmUService(mmState));
        _ipcServiceManager.RegisterService(new MmSvService(mmState));

        // PSC 电源管理服务 (共享 PscState)
        var pscState = new PscState();
        _ipcServiceManager.RegisterService(new PscMService(pscState));
        _ipcServiceManager.RegisterService(new PscCService(pscState));

        // SPL 安全平台服务 (共享 SplState)
        var splState = new SplState();
        _ipcServiceManager.RegisterService(new SplGeneralService(splState));
        _ipcServiceManager.RegisterService(new SplMigService(splState));
        _ipcServiceManager.RegisterService(new SplFsService(splState));
        _ipcServiceManager.RegisterService(new SplSslService(splState));
        _ipcServiceManager.RegisterService(new SplEsService(splState));

        // 注册 SVC 处理函数
        RegisterCoreSvcs();

        // 填充服务列表
        foreach (var service in _ipcServiceManager.GetAllServices())
        {
            _serviceEntries.Add($"  {service.PortName}");
        }

        var serviceList = this.FindControl<ListBox>("ServiceList");
        if (serviceList != null) serviceList.ItemsSource = _serviceEntries;

        var svcLogList = this.FindControl<ListBox>("SvcLogList");
        if (svcLogList != null) svcLogList.ItemsSource = _svcLogEntries;

        Logger.Info(nameof(MainWindow), "所有子系统初始化完成");
    }

    private void RegisterCoreSvcs()
    {
        // SVC 0x26: OutputDebugString
        _svcDispatcher?.Register(0x26, svc =>
        {
            // 读取调试字符串
            if (svc.X0 != 0 && _memory != null)
            {
                try
                {
                    var len = (int)svc.X2;
                    if (len > 0 && len < 1024)
                    {
                        var buf = new byte[len];
                        _memory.Read(svc.X0, buf);
                        var msg = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
                        Logger.Info("Guest", $"DebugString: {msg}");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _svcLogEntries.Add($"[Debug] {msg}");
                            if (_svcLogEntries.Count > 200) _svcLogEntries.RemoveAt(0);
                        });
                    }
                }
                catch { /* 忽略读取失败 */ }
            }
            return new SvcResult { ReturnCode = ResultCode.Success };
        });

        // SVC 0x01: SetHeapSize
        _svcDispatcher?.Register(0x01, svc =>
        {
            Logger.Debug(nameof(MainWindow), $"SetHeapSize: 0x{svc.X1:X16}");
            return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 0x1000_0000 };
        });

        // SVC 0x28: GetInfo (部分实现)
        _svcDispatcher?.Register(0x28, svc =>
        {
            var infoType = (int)svc.X2;
            if (infoType == 0) // 是否允许调用
                return new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = 1 };
            return new SvcResult { ReturnCode = ResultCode.KernelResult(TKernelResult.NotImplemented) };
        });
    }

    private void SetupUiEvents()
    {
        var openBtn = this.FindControl<Button>("OpenNroButton");
        if (openBtn != null) openBtn.Click += OnOpenNroClick;

        var pauseBtn = this.FindControl<Button>("PauseButton");
        if (pauseBtn != null) pauseBtn.Click += OnPauseClick;

        var debugBtn = this.FindControl<Button>("DebugButton");
        if (debugBtn != null) debugBtn.Click += OnDebugClick;

        // 拖放支持
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnOpenNroClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择 NRO 文件",
                AllowMultiple = false,
            });

        if (files.Count > 0)
        {
            await LoadAndRunNro(files[0].Path.LocalPath);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    var path = file.Path.LocalPath;
                    if (path.EndsWith(".nro", StringComparison.OrdinalIgnoreCase))
                    {
                        await LoadAndRunNro(path);
                        break;
                    }
                }
            }
        }
    }

    private async Task LoadAndRunNro(string filePath)
    {
        var overlay = this.FindControl<Border>("LoadingOverlay");
        if (overlay != null) overlay.IsVisible = true;

        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText != null) statusText.Text = $"正在加载: {System.IO.Path.GetFileName(filePath)}";

        try
        {
            // 在后台线程加载和运行
            await Task.Run(() =>
            {
                var nroModule = _nroLoader!.Load(filePath);

                var processInfo = new ProcessInfo
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    EntryPoint = nroModule.EntryPoint,
                };

                var process = _horizonSystem!.CreateProcess(nroModule, processInfo);
                _horizonSystem.StartProcess(process);

                // 更新 UI 信息
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var processInfoText = this.FindControl<TextBlock>("ProcessInfoText");
                    if (processInfoText != null)
                        processInfoText.Text = $"PID: {processInfo.ProcessId}\n名称: {processInfo.Name}\n入口: 0x{nroModule.EntryPoint:X16}";

                    var placeholder = this.FindControl<TextBlock>("PlaceholderText");
                    if (placeholder != null) placeholder.Text = "▶ 正在运行";

                    var regs = this.FindControl<TextBlock>("RegistersText");
                    if (regs != null) regs.Text = $"PC: 0x{process.Engine.GetPC():X16}";

                    var sp = this.FindControl<TextBlock>("SpText");
                    if (sp != null) sp.Text = $"SP: 0x{process.Engine.GetSP():X16}";

                    if (statusText != null) statusText.Text = $"运行中 — {processInfo.Name}";
                });

                // 运行 SVC 分发循环（阻塞）
                _horizonSystem.RunProcess(process);
            });
        }
        catch (Exception ex)
        {
            Logger.Error(nameof(MainWindow), $"加载 NRO 失败: {ex.Message}");
            if (statusText != null) statusText.Text = $"错误: {ex.Message}";
        }
        finally
        {
            if (overlay != null) overlay.IsVisible = false;
        }
    }

    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        _debuggerService?.Pause();
    }

    private void OnDebugClick(object? sender, RoutedEventArgs e)
    {
        // TODO: 打开调试面板
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _inputManager?.OnKeyDown(e.Key.ToString());
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _inputManager?.OnKeyUp(e.Key.ToString());
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        _horizonSystem?.Dispose();
        _renderer?.Dispose();
        _audioBackend?.Dispose();
        _memory?.Dispose();
        _debuggerService?.Dispose();
        GC.SuppressFinalize(this);
    }
}
