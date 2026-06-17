using System.Collections.ObjectModel;
using BaudioApp.Models;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BaudioApp.Services;

public class BluetoothService : IDisposable
{
    private DeviceWatcher? _watcher;
    private readonly DiagnosticService _diag;

    private static readonly string[] AdditionalProperties =
        ["System.Devices.Aep.SignalStrength"];

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = [];

    public event Action<BluetoothDeviceInfo>? DeviceAdded;
    public event Action<string>? DeviceRemoved;

    public BluetoothService(DiagnosticService diag) => _diag = diag;

    public event Action? EnumerationCompleted;

    public void Start()
    {
        string selector = AudioPlaybackConnection.GetDeviceSelector();

        _watcher = DeviceInformation.CreateWatcher(selector, AdditionalProperties);
        _watcher.Added               += OnDeviceAdded;
        _watcher.Removed             += OnDeviceRemoved;
        _watcher.Updated             += OnDeviceUpdated;
        _watcher.EnumerationCompleted += (_, _) => EnumerationCompleted?.Invoke();

        _watcher.Start();
        _diag.Info("Scanning for Bluetooth audio devices…", tags: ["System"]);
    }

    public void Stop()
    {
        if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            _watcher.Stop();
    }

    private void OnDeviceAdded(DeviceWatcher _, DeviceInformation info)
    {
        var device = new BluetoothDeviceInfo
        {
            Id = info.Id,
            Name = info.Name,
            SignalStrength = ParseSignalStrength(info.Properties)
        };
        App.Current.Dispatcher.Invoke(() => Devices.Add(device));
        DeviceAdded?.Invoke(device);
        _diag.Info($"Found: {info.Name}", tags: ["System"]);
    }

    private void OnDeviceRemoved(DeviceWatcher _, DeviceInformationUpdate update)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Id == update.Id);
            if (existing is not null) Devices.Remove(existing);
        });
        DeviceRemoved?.Invoke(update.Id);
        _diag.Info($"Device removed (id={update.Id[..8]}…)", tags: ["System"]);
    }

    private void OnDeviceUpdated(DeviceWatcher _, DeviceInformationUpdate update)
    {
        var sig = ParseSignalStrength(update.Properties);
        if (sig is null) return;

        App.Current.Dispatcher.Invoke(() =>
        {
            var device = Devices.FirstOrDefault(d => d.Id == update.Id);
            if (device is not null) device.SignalStrength = sig;
        });
    }

    private static int? ParseSignalStrength(IReadOnlyDictionary<string, object> props)
    {
        if (props.TryGetValue("System.Devices.Aep.SignalStrength", out var v) && v is int i)
            return i;
        return null;
    }

    public void Dispose() => Stop();
}
