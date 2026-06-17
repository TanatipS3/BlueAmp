using BaudioApp.Interop;

namespace BaudioApp.Services;

public class AudioMeterService : IDisposable
{
    private AudioEndpoint? _device;
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
        try { PeakChanged?.Invoke(dev.PeakValue); }
        catch { /* device may vanish mid-tick during a disconnect */ }
    }

    private void AcquireDevice()
    {
        _device = _deviceName is not null
            ? AudioDevices.FindActiveRenderByName(_deviceName)
            : AudioDevices.GetDefaultRender();
    }

    private void ReleaseDevice()
    {
        _device?.Dispose();
        _device = null;
    }

    public void Dispose() => Stop();
}
