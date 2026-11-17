using BaudioApp.Helpers;
using BaudioApp.Models;
using Windows.Media.Audio;

namespace BaudioApp.Services;

/// Owns the A2DP connection to the phone.
///
/// Threading: every member runs on the UI thread. Callers are UI code, awaits
/// resume on the UI thread's synchronization context, and the one callback Windows
/// raises on its own thread (the connection's StateChanged) is posted over before
/// it touches anything — so no state here needs a lock.
public class AudioConnectionService : IDisposable
{
    private AudioPlaybackConnection? _connection;   // the currently active (open) connection
    private CancellationTokenSource? _connectCts;   // the connect attempt in flight
    private CancellationTokenSource? _reconnectCts; // retry loop after an unexpected drop
    private readonly DiagnosticService _diag;

    public BluetoothDeviceInfo? ActiveDevice { get; private set; }
    public bool AutoReconnect { get; set; }

    public event Action<DeviceConnectionState, string?>? StateChanged;

    public AudioConnectionService(DiagnosticService diag) => _diag = diag;

    /// A connect the user (or app startup/wake) asked for. It takes over from any
    /// background retry, so automatic reconnects can never undo a manual choice.
    public Task ConnectAsync(BluetoothDeviceInfo device)
    {
        StopBackgroundRetries();
        return ConnectCoreAsync(device);
    }

    private async Task ConnectCoreAsync(BluetoothDeviceInfo device)
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
            cts.Token.ThrowIfCancellationRequested();

            var openTask = newConn.OpenAsync().AsTask(cts.Token);
            await Task.WhenAll(openTask, minDelay);
            var result = await openTask;
            cts.Token.ThrowIfCancellationRequested();

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

                    // Windows returns a specific error next to the (often generic)
                    // status — surface it instead of throwing it away.
                    var extended = result.ExtendedError;
                    int hresult = extended?.HResult ?? 0;
                    string detail = hresult != 0
                        ? $"Status: {result.Status} · {ErrorTranslator.FormatHResult(hresult)} · {extended!.Message}"
                        : $"Status: {result.Status}";
                    Fail(ErrorTranslator.Translate(result.Status, hresult), detail);
                }
            }
        }
        catch (OperationCanceledException)
        {
            DropConn(ref newConn);
            _diag.Info("Connection cancelled", tags: ["A2DP"]);

            // A newer attempt replaced this one — it owns the state from here, so
            // reporting anything now would only make the UI flicker.
            if (cts != _connectCts) return;

            // Cancelling a switch to another device leaves the old connection playing.
            bool stillConnected = _connection?.State == AudioPlaybackConnectionState.Opened;
            StateChanged?.Invoke(
                stillConnected ? DeviceConnectionState.Connected : DeviceConnectionState.Available, null);
        }
        catch (Exception ex)
        {
            DropConn(ref newConn);
            if (cts != _connectCts) return;
            Fail(ErrorTranslator.Translate(AudioPlaybackConnectionOpenResultStatus.UnknownFailure, ex.HResult),
                 $"{ErrorTranslator.FormatHResult(ex.HResult)} · {ex.Message}");
        }
    }

    public void Cancel()
    {
        StopBackgroundRetries();
        _connectCts?.Cancel();
    }

    public void Disconnect()
    {
        StopBackgroundRetries();
        _connectCts?.Cancel();
        DropConn(ref _connection);
        ActiveDevice = null;
        StateChanged?.Invoke(DeviceConnectionState.Available, null);
        _diag.Info("Disconnected", tags: ["A2DP"]);
    }

    private void StopBackgroundRetries() => _reconnectCts?.Cancel();

    private void Fail(string message, string? detail = null)
    {
        StateChanged?.Invoke(DeviceConnectionState.Error, message);
        _diag.Error(message, detail, "A2DP", "Error");
    }

    // Increasing delays: first retry is fast (normal drop), later retries
    // allow the Bluetooth stack time to reinitialize after sleep/wake.
    private static readonly int[] ReconnectDelaysMs = [1500, 4000, 8000, 15000];

    // Raised by Windows on one of its own threads — hop to the UI thread first.
    private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        if (sender.State != AudioPlaybackConnectionState.Closed) return;
        Ui.Post(() => _ = HandleDroppedAsync(sender));
    }

    private async Task HandleDroppedAsync(AudioPlaybackConnection sender)
    {
        // Connections we closed ourselves (disconnect, switch, failed attempt) are no
        // longer the active one by the time this runs — only a real drop gets past here.
        if (sender != _connection) return;

        _diag.Warn("Audio connection dropped", tags: ["A2DP"]);

        var device = ActiveDevice;
        DropConn(ref _connection);

        if (!AutoReconnect || device is null)
        {
            ActiveDevice = null;
            StateChanged?.Invoke(DeviceConnectionState.Error, "Connection dropped unexpectedly.");
            return;
        }

        await RunReconnectLoopAsync(device);
    }

    /// True while the retry schedule below is running.
    public bool IsReconnecting => _reconnectCts is { IsCancellationRequested: false };

    /// Reconnect using the same patient retry schedule as a dropped connection.
    /// Used after wake-from-sleep, when the Bluetooth stack may need several seconds
    /// before a connect can succeed — a single attempt would often just fail.
    public Task ReconnectAsync(BluetoothDeviceInfo device) => RunReconnectLoopAsync(device);

    private async Task RunReconnectLoopAsync(BluetoothDeviceInfo device)
    {
        _reconnectCts?.Cancel();
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        var token = cts.Token;

        // Show "reconnecting" straight away instead of a stale "Connected".
        StateChanged?.Invoke(DeviceConnectionState.Connecting, null);

        try
        {
            for (int i = 0; i < ReconnectDelaysMs.Length; i++)
            {
                await Task.Delay(ReconnectDelaysMs[i], token);

                _diag.Info($"Auto-reconnect attempt {i + 1}/{ReconnectDelaysMs.Length}…", tags: ["A2DP"]);
                await ConnectCoreAsync(device);

                if (token.IsCancellationRequested) return;   // the user took over meanwhile
                if (ActiveDevice is not null) return;        // succeeded
            }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            // Mark the loop finished (unless a newer loop already replaced it).
            if (_reconnectCts == cts) _reconnectCts = null;
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
        StopBackgroundRetries();
        _connection?.Dispose();
        _connection = null;
    }
}
