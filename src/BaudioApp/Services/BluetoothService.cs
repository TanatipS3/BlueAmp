using System.Collections.ObjectModel;
using BaudioApp.Helpers;
using BaudioApp.Models;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BaudioApp.Services;

public class BluetoothService : IDisposable
{
    private const int RestartDelayMs = 3000;

    private DeviceWatcher? _watcher;
    private readonly DiagnosticService _diag;
    private volatile bool _stopping;

    /// Only ever touched on the UI thread.
    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = [];

    public event Action<BluetoothDeviceInfo>? DeviceAdded;
    public event Action<string>? DeviceRemoved;

    /// Raised on the UI thread once the initial device list is complete.
    public event Action? EnumerationCompleted;

    public BluetoothService(DiagnosticService diag) => _diag = diag;

    public void Start()
    {
        _stopping = false;
        string selector = AudioPlaybackConnection.GetDeviceSelector();

        _watcher = DeviceInformation.CreateWatcher(selector);
        _watcher.Added   += OnDeviceAdded;
        _watcher.Removed += OnDeviceRemoved;
        _watcher.Updated += (_, _) => { };   // must be subscribed for Removed to be delivered
        // Posted so it runs after the (also posted) device additions that preceded it.
        _watcher.EnumerationCompleted += (_, _) => Ui.Post(() => EnumerationCompleted?.Invoke());
        _watcher.Stopped += OnWatcherStopped;

        _watcher.Start();
        _diag.Info("Scanning for Bluetooth audio devices…", tags: ["System"]);
    }

    public void Stop()
    {
        _stopping = true;
        if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            _watcher.Stop();
    }

    /// The watcher can stop on its own (Windows aborts it when the Bluetooth stack
    /// restarts). Without this the device list would simply go stale until the app
    /// was restarted, so start it again after a short pause.
    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        if (_stopping) return;
        _diag.Warn("Bluetooth device watcher stopped — restarting", tags: ["System"]);

        Task.Delay(RestartDelayMs).ContinueWith(_ =>
        {
            if (_stopping) return;
            try
            {
                if (sender.Status is DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted)
                    sender.Start();
            }
            catch (Exception ex)
            {
                _diag.Error("Could not restart the Bluetooth device watcher", ex.Message, "System");
            }
        });
    }

    private void OnDeviceAdded(DeviceWatcher _, DeviceInformation info)
    {
        var device = new BluetoothDeviceInfo { Id = info.Id, Name = info.Name };

        // Post, never Send: the watcher's thread must not wait on the UI thread.
        Ui.Post(() =>
        {
            // A restarted watcher re-reports every device; keep the existing entry
            // (the view-model may be holding it as the connected device).
            if (Devices.Any(d => d.Id == device.Id)) return;
            Devices.Add(device);
            DeviceAdded?.Invoke(device);
        });
        _diag.Info($"Found: {info.Name}", tags: ["System"]);
    }

    private void OnDeviceRemoved(DeviceWatcher _, DeviceInformationUpdate update)
    {
        Ui.Post(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Id == update.Id);
            if (existing is not null) Devices.Remove(existing);
            DeviceRemoved?.Invoke(update.Id);
        });
        _diag.Info("Device removed", update.Id, "System");
    }

    public void Dispose() => Stop();
}
