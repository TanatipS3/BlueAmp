using BaudioApp.Helpers;
using BaudioApp.Models;
using Windows.Media.Audio;

namespace BaudioApp.Services;

public class AudioConnectionService : IDisposable
{
    private AudioPlaybackConnection? _connection;   // the currently active (open) connection
    private CancellationTokenSource? _connectCts;
    private readonly DiagnosticService _diag;

    public BluetoothDeviceInfo? ActiveDevice { get; private set; }
    public bool AutoReconnect { get; set; }

    public event Action<DeviceConnectionState, string?>? StateChanged;

    public AudioConnectionService(DiagnosticService diag) => _diag = diag;

    public async Task ConnectAsync(BluetoothDeviceInfo device)
    {
        _connectCts?.Cancel();
        _connectCts = new CancellationTokenSource();
        var cts = _connectCts;

        StateChanged?.Invoke(DeviceConnectionState.Connecting, null);
        _diag.Info($"Connecting to {device.Name}…", tags: ["A2DP"]);

        AudioPlaybackConnection? newConn = null;

        try
        {
            if (cts.IsCancellationRequested) return;

            newConn = AudioPlaybackConnection.TryCreateFromId(device.Id);
            if (newConn is null)
            {
                Fail("Could not create audio connection. The device may not support A2DP.");
                return;
            }

            newConn.StateChanged += OnConnectionStateChanged;

            var minDelay = Task.Delay(800, cts.Token);
            await newConn.StartAsync().AsTask(cts.Token);

            if (cts.IsCancellationRequested) { DropConn(ref newConn); return; }

            var openTask = newConn.OpenAsync().AsTask(cts.Token);
            await Task.WhenAll(openTask, minDelay);
            var result = await openTask;

            if (cts.IsCancellationRequested) { DropConn(ref newConn); return; }

            if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                // New connection established — safe to release the old one now
                DropConn(ref _connection);
                _connection = newConn;
                newConn = null;
                ActiveDevice = device;

                StateChanged?.Invoke(DeviceConnectionState.Connected, null);
                _diag.Success($"Connected to {device.Name}", tags: ["A2DP"]);

                // Some phones don't start streaming until they receive an AVRCP volume
                // command from the PC. Send a 1% invisible nudge to trigger that handshake.
                _ = AudioStreamActivator.ActivateAsync(device.Name, cts.Token);
            }
            else
            {
                // New connection failed — dispose it and check if the old one survived
                DropConn(ref newConn);

                if (_connection?.State == AudioPlaybackConnectionState.Opened)
                {
                    _diag.Warn($"Failed to connect {device.Name} — previous connection still active", tags: ["A2DP"]);
                    StateChanged?.Invoke(DeviceConnectionState.Connected, null);
                }
                else
                {
                    DropConn(ref _connection);
                    ActiveDevice = null;
                    Fail(ErrorTranslator.Translate(result.Status), $"Status: {result.Status}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            DropConn(ref newConn);
            _diag.Info("Connection cancelled", tags: ["A2DP"]);
            StateChanged?.Invoke(DeviceConnectionState.Available, null);
        }
        catch (Exception ex)
        {
            DropConn(ref newConn);
            Fail(ErrorTranslator.Translate(AudioPlaybackConnectionOpenResultStatus.UnknownFailure), ex.Message);
        }
    }

    public void Cancel() => _connectCts?.Cancel();

    public void Disconnect()
    {
        _connectCts?.Cancel();
        DropConn(ref _connection);
        ActiveDevice = null;
        StateChanged?.Invoke(DeviceConnectionState.Available, null);
        _diag.Info("Disconnected", tags: ["A2DP"]);
    }

    private void Fail(string message, string? detail = null)
    {
        StateChanged?.Invoke(DeviceConnectionState.Error, message);
        _diag.Error(message, detail, "A2DP", "Error");
    }

    // Increasing delays: first retry is fast (normal drop), later retries
    // allow the Bluetooth stack time to reinitialize after sleep/wake.
    private static readonly int[] ReconnectDelaysMs = [1500, 4000, 8000, 15000];

    private async void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        if (sender != _connection) return;
        if (sender.State != AudioPlaybackConnectionState.Closed) return;
        if (_connectCts?.IsCancellationRequested == true) return;

        _diag.Warn("Audio connection dropped", tags: ["A2DP"]);

        if (!AutoReconnect || ActiveDevice is null)
        {
            StateChanged?.Invoke(DeviceConnectionState.Error, "Connection dropped unexpectedly.");
            return;
        }

        var device = ActiveDevice;
        for (int i = 0; i < ReconnectDelaysMs.Length; i++)
        {
            await Task.Delay(ReconnectDelaysMs[i]);
            if (_connectCts?.IsCancellationRequested == true || ActiveDevice is null) return;

            _diag.Info($"Auto-reconnect attempt {i + 1}/{ReconnectDelaysMs.Length}…", tags: ["A2DP"]);
            await ConnectAsync(device);
            if (ActiveDevice is not null) return; // succeeded
        }

        _diag.Error("Auto-reconnect failed after all attempts", null, "A2DP", "Error");
    }

    private static void DropConn(ref AudioPlaybackConnection? conn)
    {
        if (conn is null) return;
        conn.Dispose();
        conn = null;
    }

    public void Dispose()
    {
        _connectCts?.Cancel();
        _connection?.Dispose();
        _connection = null;
    }
}
