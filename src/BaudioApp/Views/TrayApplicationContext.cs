using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using BaudioApp.Helpers;
using BaudioApp.Models;
using BaudioApp.Services;
using BaudioApp.ViewModels;

namespace BaudioApp.Views;

/// The application's root — owns the tray icon, services, view-model, and the
/// lazily-created popup/settings/diagnostics windows. Replaces the old WPF App.
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly DiagnosticService _diag;
    private readonly SettingsService _settings;
    private readonly BluetoothService _bluetooth;
    private readonly AudioConnectionService _audioConn;
    private readonly AudioMeterService _audioMeter;
    private readonly TrayPopupViewModel _vm;

    private readonly NotifyIcon _tray;
    private readonly MessageWindow _msg;
    private readonly System.Windows.Forms.Timer _resourceTimer;

    private PopupForm? _popup;
    private SettingsForm? _settingsForm;
    private DiagnosticsForm? _diagForm;

    public TrayApplicationContext()
    {
        _diag = new DiagnosticService();
        _msg  = new MessageWindow();
        Ui.Initialize(_msg);

        Application.ThreadException += (_, e) =>
            _diag.Error(e.Exception.Message, e.Exception.ToString(), "System", "Crash");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _diag.Error((e.ExceptionObject as Exception)?.Message ?? "Fatal error",
                        e.ExceptionObject?.ToString(), "System", "Crash");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _diag.Error(e.Exception.Message, e.Exception.ToString(), "System", "Crash");
            e.SetObserved();
        };

        _settings = new SettingsService();
        _settings.Load();
        _settings.ApplyStartup();
        _diag.VerboseEnabled = _settings.Current.KeepLogHistory;

        _bluetooth  = new BluetoothService(_diag);
        _audioConn  = new AudioConnectionService(_diag) { AutoReconnect = _settings.Current.AutoReconnect };
        _audioMeter = new AudioMeterService();

        _vm = new TrayPopupViewModel(_bluetooth, _audioConn, _audioMeter);
        _vm.SettingsRequested    += ShowSettings;
        _vm.DiagnosticsRequested += ShowDiagnostics;
        _vm.TrayIconStateChanged += OnTrayIconStateChanged;

        _tray = new NotifyIcon
        {
            Visible          = true,
            Text             = "BlueAmp — No device connected",
            Icon             = LoadIcon("tray-idle.ico"),
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseClick += OnTrayClick;

        _bluetooth.EnumerationCompleted += OnEnumerationCompleted;
        _bluetooth.Start();

        _msg.Resumed += OnSystemResumed;

        ShortcutHelper.EnsureDesktopShortcut();

        _resourceTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromMinutes(5).TotalMilliseconds
        };
        _resourceTimer.Tick += (_, _) => CheckResources();
        _resourceTimer.Start();

        _diag.Info("BlueAmp started", tags: ["System"]);
    }

    // ── Tray interaction ──────────────────────────────────────────────────────

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_popup is { Visible: true }) { _popup.Hide(); return; }
        _popup ??= new PopupForm(_vm);
        _popup.ShowNearTray();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Settings",    null, (_, _) => ShowSettings());
        menu.Items.Add("Diagnostics", null, (_, _) => ShowDiagnostics());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",        null, (_, _) => ExitApp());
        return menu;
    }

    // ── Auto-connect / last device / sleep-wake (ported from WPF App) ──────────

    private void OnEnumerationCompleted()
    {
        if (_settings.Current.RememberLastDevice != true) return;
        if (_settings.Current.LastDeviceId is null) return;

        var device = _bluetooth.Devices.FirstOrDefault(d => d.Id == _settings.Current.LastDeviceId);
        if (device is null) return;

        _diag.Info($"Restoring last session — connecting to {device.Name}", tags: ["System"]);
        Ui.Post(() => _ = _vm.ConnectCommand.ExecuteAsync(device));
    }

    private void OnTrayIconStateChanged(DeviceConnectionState state)
    {
        if (state == DeviceConnectionState.Connected
            && _settings.Current.RememberLastDevice == true
            && _vm.ConnectedDevice is { } dev)
        {
            _settings.Current.LastDeviceId = dev.Id;
            _settings.Save();
        }

        UpdateTrayIcon(state);
    }

    private void OnSystemResumed()
    {
        _diag.Info("System resumed from sleep", tags: ["System"]);

        if (_settings.Current.RememberLastDevice != true) return;
        if (_settings.Current.LastDeviceId is null) return;
        if (_vm.OverallState == DeviceConnectionState.Connecting) return;

        Task.Delay(4000).ContinueWith(_ => Ui.Post(() =>
        {
            if (_vm.OverallState == DeviceConnectionState.Connected) return;
            var device = _bluetooth.Devices.FirstOrDefault(d => d.Id == _settings.Current.LastDeviceId);
            if (device is null) return;
            _diag.Info($"Reconnecting after wake — {device.Name}", tags: ["System"]);
            _ = _vm.ConnectCommand.ExecuteAsync(device);
        }));
    }

    private void CheckResources()
    {
        long memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        if (memMb > 120)
            _diag.Warn($"Memory usage high: {memMb} MB", tags: ["System"]);
    }

    // ── Tray icon visuals ─────────────────────────────────────────────────────

    private void UpdateTrayIcon(DeviceConnectionState state)
    {
        var iconName = state switch
        {
            DeviceConnectionState.Connected => "tray-connected.ico",
            DeviceConnectionState.Error     => "tray-error.ico",
            _                               => "tray-idle.ico"
        };
        var tooltip = state switch
        {
            DeviceConnectionState.Connected  => $"BlueAmp — {_vm.ConnectedDevice?.Name ?? "Connected"}",
            DeviceConnectionState.Connecting => "BlueAmp — Connecting…",
            DeviceConnectionState.Error      => "BlueAmp — Connection failed",
            _                                => "BlueAmp — No device connected"
        };

        var old = _tray.Icon;
        _tray.Icon = LoadIcon(iconName);
        old?.Dispose();
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private static System.Drawing.Icon LoadIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        try { if (File.Exists(path)) return new System.Drawing.Icon(path, 16, 16); }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    // ── Windows ───────────────────────────────────────────────────────────────

    private void ShowSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(new SettingsViewModel(_settings), OnSettingsSaved);
        }
        _settingsForm.Show();
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    private void OnSettingsSaved()
    {
        _diag.VerboseEnabled    = _settings.Current.KeepLogHistory;
        _audioConn.AutoReconnect = _settings.Current.AutoReconnect;
    }

    private void ShowDiagnostics()
    {
        if (_diagForm is null || _diagForm.IsDisposed)
            _diagForm = new DiagnosticsForm(new DiagnosticViewModel(_diag));
        _diagForm.Show();
        _diagForm.WindowState = FormWindowState.Normal;
        _diagForm.Activate();
    }

    // ── Lifetime ──────────────────────────────────────────────────────────────

    private void ExitApp()
    {
        Cleanup();
        ExitThread();
    }

    private void Cleanup()
    {
        _resourceTimer.Stop();
        _resourceTimer.Dispose();
        _bluetooth.Dispose();
        _audioConn.Dispose();
        _audioMeter.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _popup?.Dispose();
        _settingsForm?.Dispose();
        _diagForm?.Dispose();
        _msg.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cleanup();
        base.Dispose(disposing);
    }
}
