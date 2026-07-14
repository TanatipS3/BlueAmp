using BaudioApp.Interop;

namespace BaudioApp.Services;

public class AudioMeterService : IDisposable
{
    private AudioEndpoint? _device;
    private System.Windows.Forms.Timer? _timer;
    private string? _deviceName;
    private bool _running;

    public event Action<float>? PeakChanged;

    /// Remembers which device to meter, but only spins up the timer when Resume()
    /// is called (i.e. when the popup is actually visible).
    public void Start(string? deviceFriendlyName = null)
    {
        _deviceName = deviceFriendlyName;
    }

    /// Must be called on the UI thread. The MMDevice COM objects are apartment-
    /// bound (no proxy/stub), so reading PeakValue from a threadpool thread throws
    /// E_NOINTERFACE — a WinForms timer keeps every Core Audio call on this thread.
    public void Resume()
    {
        if (_running) return;
        _running = true;

        AcquireDevice();
        if (_device is null) { _running = false; return; }

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += OnTick;
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

    private void OnTick(object? sender, EventArgs e)
    {
        var dev = _device;
        if (dev is null) return;
        try { PeakChanged?.Invoke(dev.PeakValue); }
        catch { /* device may vanish mid-tick during a disconnect */ }
    }

    private void AcquireDevice()
    {
        // In A2DP-sink mode the phone's audio is rendered through the PC's own
        // output device — a render endpoint named after the phone usually doesn't
        // exist. Prefer a name match (covers headphone-style endpoints), then
        // fall back to the default render endpoint, which is what actually plays.
        _device = (_deviceName is not null ? AudioDevices.FindActiveRenderByName(_deviceName) : null)
                  ?? AudioDevices.GetDefaultRender();
    }

    private void ReleaseDevice()
    {
        _device?.Dispose();
        _device = null;
    }

    public void Dispose() => Stop();
}
