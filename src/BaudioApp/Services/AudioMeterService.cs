using NAudio.CoreAudioApi;

namespace BaudioApp.Services;

public class AudioMeterService : IDisposable
{
    private MMDevice? _device;
    private System.Timers.Timer? _timer;
    private string? _deviceName;
    private bool _running;

    public event Action<float>? PeakChanged;

    /// Remembers which device to meter, but only spins up the timer when Resume()
    /// is called (i.e. when the popup is actually visible).
    public void Start(string? deviceFriendlyName = null)
    {
        _deviceName = deviceFriendlyName;
    }

    public void Resume()
    {
        if (_running) return;
        _running = true;

        AcquireDevice();
        if (_device is null) { _running = false; return; }

        _timer = new System.Timers.Timer(100);
        _timer.Elapsed += OnTick;
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Pause()
    {
        _running = false;
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        ReleaseDevice();
    }

    public void Stop()
    {
        _deviceName = null;
        Pause();
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var dev = _device;
        if (dev is null) return;
        try { PeakChanged?.Invoke(dev.AudioMeterInformation.MasterPeakValue); }
        catch { /* device may vanish mid-tick during a disconnect */ }
    }

    private void AcquireDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        _device = _deviceName is not null
            ? FindByName(enumerator, _deviceName)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private void ReleaseDevice()
    {
        _device?.Dispose();
        _device = null;
    }

    private static MMDevice? FindByName(MMDeviceEnumerator enumerator, string name)
    {
        var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return collection.FirstOrDefault(d => d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => Stop();
}
