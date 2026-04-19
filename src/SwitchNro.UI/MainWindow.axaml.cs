using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private IpcBridge? _ipcBridge;
    private Sdl2AudioBackend? _audioBackend;
    private MetalRenderer? _renderer;
    private WriteableBitmap? _bitmap;

    // UI 数据绑定
    private readonly ObservableCollection<string> _svcLogEntries = new();
    private readonly ObservableCollection<string> _serviceEntries = new();

    private EmulatorConfig? _config;

    public MainWindow()
    {
        InitializeComponent();
        InitializeSubsystem();
        SetupUiEvents();
        SetupRendering();
    }

    private void InitializeSubsystem()
    {
        _config = ConfigManager.Load();
        Logger.SetMinimumLevel(LogLevel.Debug); // 开启详细调试
        _memory = new VirtualMemoryManager();
        _svcDispatcher = new SvcDispatcher();
        _nroLoader = new NroLoader.NroLoader(_memory);
        _inputManager = new InputManager();
        _debuggerService = new DebuggerService();
        _ipcServiceManager = new IpcServiceManager();
        _ipcBridge = new IpcBridge(_ipcServiceManager, _memory);

        // SmService.GetService 通过 HandleTable 创建真实的 KClientSession 句柄
        // HandleTable 在 BindProcess 时动态设置（进程创建后才有句柄表）

        _ipcServiceManager.RegisterService(new SmService(_ipcServiceManager));
        _ipcServiceManager.RegisterService(new FsService());
        _ipcServiceManager.RegisterService(new ViService("vi:m"));
        _ipcServiceManager.RegisterService(new ViService("vi:u"));
        _ipcServiceManager.RegisterService(new ViService("vi:s"));
        _ipcServiceManager.RegisterService(new HidService());
        _ipcServiceManager.RegisterService(new NvService("nvdrv:a", _ipcServiceManager));
        _ipcServiceManager.RegisterService(new NvService("nvdrv:s", _ipcServiceManager));
        _ipcServiceManager.RegisterService(new NvService("nvdrv:t", _ipcServiceManager));
        _ipcServiceManager.RegisterService(new NvMemPService());
        _ipcServiceManager.RegisterService(new AmService(_ipcServiceManager));
        _ipcServiceManager.RegisterService(new AppletAeService());

        _audioBackend = new Sdl2AudioBackend();
        _renderer = new MetalRenderer();
        _horizonSystem = new HorizonSystem(_memory, _svcDispatcher);

        RegisterCoreSvcs();
        Logger.Info(nameof(MainWindow), "所有子系统初始化完成");
    }

    private void SetupRendering()
    {
        var service = _ipcServiceManager?.GetService("vi:m");
        if (service is ViService viService)
        {
            viService.FramePresented += OnFramePresented;
        }
    }

    private void OnFramePresented(int width, int height, ReadOnlySpan<byte> frameData)
    {
        // Span 不能直接在 lambda 中捕获，必须先转为普通数组
        byte[] dataCopy = frameData.ToArray();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            {
                _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
                var gameView = this.FindControl<Image>("GameView");
                if (gameView != null) gameView.Source = _bitmap;
                var placeholder = this.FindControl<TextBlock>("PlaceholderText");
                if (placeholder != null) placeholder.IsVisible = false;
            }

            using (var buffer = _bitmap.Lock())
            {
                Marshal.Copy(dataCopy, 0, buffer.Address, dataCopy.Length);
            }
            this.FindControl<Image>("GameView")?.InvalidateVisual();
        });
    }

    private void RegisterCoreSvcs()
    {
        _svcDispatcher?.Register(0x26, svc => {
            if (svc.X0 != 0 && _memory != null) {
                try {
                    var len = (int)svc.X1;
                    if (len > 0 && len < 1024) {
                        var buf = new byte[len];
                        _memory.Read(svc.X0, buf);
                        var msg = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _svcLogEntries.Add($"[Debug] {msg}"));
                    }
                } catch {}
            }
            return new SvcResult { ReturnCode = ResultCode.Success };
        });

        _svcDispatcher?.Register(0x01, svc => _horizonSystem!.SetHeapSize(svc));
        _svcDispatcher?.Register(0x05, svc => _horizonSystem!.QueryMemory(svc));
        _svcDispatcher?.Register(0x06, svc => _horizonSystem!.ExitProcess(svc));
        _svcDispatcher?.Register(0x0B, svc => new SvcResult { ReturnCode = ResultCode.Success });
        _svcDispatcher?.Register(0x13, svc => new SvcResult { ReturnCode = ResultCode.Success, ReturnValue1 = (ulong)DateTimeOffset.UtcNow.Ticks });
        _svcDispatcher?.Register(0x1F, svc => _ipcBridge!.ConnectToNamedPort(svc));
        _svcDispatcher?.Register(0x21, svc => _ipcBridge!.SendSyncRequest(svc));
        _svcDispatcher?.Register(0x22, svc => _ipcBridge!.SendSyncRequestWithUserBuffer(svc));
        _svcDispatcher?.Register(0x29, svc => _horizonSystem!.GetInfo(svc));
        _svcDispatcher?.Register(0x19, svc => _horizonSystem!.CloseHandle(svc));
    }

    private void SetupUiEvents()
    {
        var openBtn = this.FindControl<Button>("OpenNroButton");
        if (openBtn != null) openBtn.Click += OnOpenNroClick;
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnOpenNroClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = "选择 NRO" });
        if (files.Count > 0) await LoadAndRunNro(files[0].Path.LocalPath);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files != null) foreach (var file in files) if (file.Path.LocalPath.EndsWith(".nro", StringComparison.OrdinalIgnoreCase)) await LoadAndRunNro(file.Path.LocalPath);
    }

    private async Task LoadAndRunNro(string filePath)
    {
        try {
            // 在加载新 NRO 前，确保旧进程已被释放
            _horizonSystem?.Dispose();
            // 重新创建一个空的系统实例，避免旧的 vCPU 残留
            _horizonSystem = new HorizonSystem(_memory!, _svcDispatcher!);

            await Task.Run(() => {
                var nroModule = _nroLoader!.Load(filePath);
                var process = _horizonSystem!.CreateProcess(nroModule, new ProcessInfo { Name = "App", EntryPoint = nroModule.EntryPoint });
                _horizonSystem.StartProcess(process);
                _ipcBridge?.BindProcess(process);
                _horizonSystem.RunProcess(process);
            });
        } catch (Exception ex) { Logger.Error("UI", ex.Message); }
    }

    public void Dispose()
    {
        _horizonSystem?.Dispose();
        _memory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
