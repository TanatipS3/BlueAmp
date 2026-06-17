using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BaudioApp.Helpers;
using BaudioApp.Models;
using BaudioApp.Services;
using BaudioApp.ViewModels;
using BaudioApp.Views;
using H.NotifyIcon;

namespace BaudioApp;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private DiagnosticService? _diag;
    private BluetoothService? _bluetooth;
    private AudioConnectionService? _audioConn;
    private AudioMeterService? _audioMeter;
    private SettingsService? _settings;
    private TrayPopupViewModel? _vm;

    private DiagnosticWindow? _diagWindow;
    private SettingsWindow? _settingsWindow;

    // Hidden window used only to receive Win32 broadcast messages (WM_POWERBROADCAST)
    private Window? _msgWindow;

    // Resource monitor: logs memory usage periodically
    private System.Timers.Timer? _resourceTimer;

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "BlueAmp_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _diag = new DiagnosticService();
        // Logging is opt-in: off by default for a lighter footprint; errors are
        // always recorded regardless so failures can still be diagnosed.

        DispatcherUnhandledException += (_, ex) =>
        {
            _diag.Error(ex.Exception.Message, ex.Exception.ToString(), "System", "Crash");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            _diag.Error(ex.ExceptionObject?.ToString() ?? "Fatal error", null, "System", "Crash");
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            _diag.Error(ex.Exception.Message, ex.Exception.ToString(), "System", "Crash");
            ex.SetObserved();
        };

        _settings = new SettingsService();
        _settings.Load();
        _settings.ApplyStartup();   // keep registry in sync with current exe path on every launch
        _diag.VerboseEnabled = _settings.Current.KeepLogHistory;

        _bluetooth  = new BluetoothService(_diag);
        _audioConn  = new AudioConnectionService(_diag) { AutoReconnect = _settings.Current.AutoReconnect };
        _audioMeter = new AudioMeterService();

        _vm = new TrayPopupViewModel(_bluetooth, _audioConn, _audioMeter);
        _vm.SettingsRequested    += ShowSettings;
        _vm.DiagnosticsRequested += ShowDiagnostics;
        _vm.TrayIconStateChanged += OnTrayIconStateChanged;

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "BlueAmp — No device connected",
            TrayPopup   = new TrayPopup { DataContext = _vm },
            DataContext  = _vm
        };

        SetTrayIcon("tray-idle.ico");
        _trayIcon.ForceCreate();
        BuildContextMenu();

        // Auto-connect to last device once the watcher finishes its initial scan
        _bluetooth.EnumerationCompleted += OnBluetoothEnumerationCompleted;

        _bluetooth.Start();

        ShortcutHelper.EnsureDesktopShortcut();

        SetupPowerMonitoring();
        SetupResourceMonitor();

        _diag.Info("BlueAmp started", tags: ["System"]);
    }

    // ── Phase 1: auto-reconnect on startup ────────────────────────────────────

    private void OnBluetoothEnumerationCompleted()
    {
        if (_settings?.Current.RememberLastDevice != true) return;
        if (_settings.Current.LastDeviceId is null) return;

        var device = _bluetooth?.Devices.FirstOrDefault(d => d.Id == _settings.Current.LastDeviceId);
        if (device is null) return;

        _diag?.Info($"Restoring last session — connecting to {device.Name}", tags: ["System"]);
        Dispatcher.InvokeAsync(() => _vm?.ConnectCommand.ExecuteAsync(device));
    }

    private void OnTrayIconStateChanged(DeviceConnectionState state)
    {
        // Save last device on every successful connection
        if (state == DeviceConnectionState.Connected
            && _settings?.Current.RememberLastDevice == true
            && _vm?.ConnectedDevice is { } dev)
        {
            _settings.Current.LastDeviceId = dev.Id;
            _settings.Save();
        }

        UpdateTrayIcon(state);
    }

    // ── Phase 3: sleep / wake resilience ──────────────────────────────────────

    private const int WM_POWERBROADCAST      = 0x0218;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_APMRESUMESUSPEND   = 0x0007;

    private void SetupPowerMonitoring()
    {
        // A 0×0 off-screen WPF window to receive broadcast messages.
        // WinForms SystemEvents is not available without System.Windows.Extensions;
        // hooking WM_POWERBROADCAST via HwndSource is the pure-WPF equivalent.
        _msgWindow = new Window
        {
            Title         = "BlueAmp_msg",
            Width         = 0,
            Height        = 0,
            Left          = -32000,
            Top           = -32000,
            WindowStyle   = WindowStyle.None,
            ShowInTaskbar = false,
            ResizeMode    = ResizeMode.NoResize
        };
        _msgWindow.SourceInitialized += (_, _) =>
        {
            var src = (HwndSource)PresentationSource.FromVisual(_msgWindow)!;
            src.AddHook(OnWin32Message);
        };
        _msgWindow.Show();
    }

    private IntPtr OnWin32Message(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST)
        {
            int ev = wParam.ToInt32();
            if (ev == PBT_APMRESUMEAUTOMATIC || ev == PBT_APMRESUMESUSPEND)
                OnSystemResumed();
        }
        return IntPtr.Zero;
    }

    private void OnSystemResumed()
    {
        _diag?.Info("System resumed from sleep", tags: ["System"]);

        if (_settings?.Current.RememberLastDevice != true) return;
        if (_settings.Current.LastDeviceId is null) return;
        if (_vm?.OverallState == DeviceConnectionState.Connecting) return; // already retrying

        // Wait 4s for the Bluetooth stack to reinitialize, then reconnect
        Task.Delay(4000).ContinueWith(_ =>
        {
            if (_vm?.OverallState == DeviceConnectionState.Connected) return;

            var device = _bluetooth?.Devices.FirstOrDefault(d => d.Id == _settings.Current.LastDeviceId);
            if (device is null) return;

            _diag?.Info($"Reconnecting after wake — {device.Name}", tags: ["System"]);
            Dispatcher.InvokeAsync(() => _vm?.ConnectCommand.ExecuteAsync(device));
        });
    }

    // ── Phase 4: resource monitor ──────────────────────────────────────────────

    private void SetupResourceMonitor()
    {
        _resourceTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _resourceTimer.Elapsed += (_, _) => CheckResources();
        _resourceTimer.AutoReset = true;
        _resourceTimer.Start();

        // Log once at startup
        CheckResources();
    }

    private void CheckResources()
    {
        var proc   = Process.GetCurrentProcess();
        long memMb = proc.WorkingSet64 / (1024 * 1024);

        // Only log when over the threshold — no point adding a routine line every
        // 5 minutes for the life of the session.
        if (memMb > 120)
            _diag?.Warn($"Memory usage high: {memMb} MB", tags: ["System"]);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();

        void Add(string header, Action handler)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => handler();
            menu.Items.Add(item);
        }

        Add("Settings",    ShowSettings);
        Add("Diagnostics", ShowDiagnostics);
        menu.Items.Add(new Separator());
        Add("Exit", ExitApp);

        _trayIcon!.ContextMenu = menu;
    }

    private void UpdateTrayIcon(DeviceConnectionState state)
    {
        var iconName = state switch
        {
            DeviceConnectionState.Connected  => "tray-connected.ico",
            DeviceConnectionState.Error      => "tray-error.ico",
            _                                => "tray-idle.ico"
        };

        var tooltip = state switch
        {
            DeviceConnectionState.Connected  => $"BlueAmp — {_vm?.ConnectedDevice?.Name ?? "Connected"}",
            DeviceConnectionState.Connecting => "BlueAmp — Connecting…",
            DeviceConnectionState.Error      => "BlueAmp — Connection failed",
            _                                => "BlueAmp — No device connected"
        };

        SetTrayIcon(iconName);
        _trayIcon!.ToolTipText = tooltip;
    }

    private void SetTrayIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (File.Exists(path))
            _trayIcon!.Icon = new System.Drawing.Icon(path, 16, 16);
    }

    private void ShowDiagnostics()
    {
        if (_diag is null) return;
        if (_diagWindow is null || !_diagWindow.IsLoaded)
            _diagWindow = new DiagnosticWindow { DataContext = new DiagnosticViewModel(_diag) };
        _diagWindow.Show();
        _diagWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_settings is null) return;
        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            var vm = new SettingsViewModel(_settings);
            vm.Saved += () =>
            {
                // Apply settings that affect running services immediately.
                if (_diag is not null)      _diag.VerboseEnabled = _settings.Current.KeepLogHistory;
                if (_audioConn is not null) _audioConn.AutoReconnect = _settings.Current.AutoReconnect;
                _settingsWindow?.Close();
            };
            _settingsWindow = new SettingsWindow { DataContext = vm };
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        Cleanup();
        Shutdown();
    }

    private void Cleanup()
    {
        _resourceTimer?.Stop();
        _resourceTimer?.Dispose();
        _msgWindow?.Close();
        _bluetooth?.Dispose();
        _audioConn?.Dispose();
        _audioMeter?.Dispose();
        _trayIcon?.Dispose();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }
}
