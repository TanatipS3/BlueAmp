using BlueAmp.Interop;

namespace BlueAmp.Services;

public class AudioMeterService : IDisposable
{
    private AudioEndpoint? _output;        // what the PC is playing (fallback)
    private AudioEndpoint? _phoneStream;   // the phone's own incoming stream, when exposed
    private bool _phoneStreamProven;       // it has reported a real level at least once
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

        AcquireDevices();
        if (_output is null && _phoneStream is null) { _running = false; return; }

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
        ReleaseDevices();
    }

    public void Stop()
    {
        _deviceName = null;
        Pause();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            // Prefer the phone's own stream: it moves only for the phone's audio,
            // whereas the PC output moves for every sound the PC makes. Windows only
            // meters a capture endpoint while something is reading it, so trust it
            // once it has shown a real level and use the output device until then.
            float phone = _phoneStream?.PeakValue ?? 0f;
            if (phone > 0.001f) _phoneStreamProven = true;

            float peak = _phoneStreamProven ? phone : (_output?.PeakValue ?? 0f);
            PeakChanged?.Invoke(peak);
        }
        catch { /* device may vanish mid-tick during a disconnect */ }
    }

    private void AcquireDevices()
    {
        if (_deviceName is not null)
            _phoneStream = AudioDevices.FindPhoneStream(_deviceName);

        // In A2DP-sink mode the phone's audio is rendered through the PC's own
        // output device — a render endpoint named after the phone usually doesn't
        // exist. Prefer a name match (covers headphone-style endpoints), then
        // fall back to the default render endpoint, which is what actually plays.
        _output = (_deviceName is not null ? AudioDevices.FindActiveRenderByName(_deviceName) : null)
                  ?? AudioDevices.GetDefaultRender();
    }

    private void ReleaseDevices()
    {
        _output?.Dispose();
        _output = null;
        _phoneStream?.Dispose();
        _phoneStream = null;
        _phoneStreamProven = false;
    }

    public void Dispose() => Stop();
}
