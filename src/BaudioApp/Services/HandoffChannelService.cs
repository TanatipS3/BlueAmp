using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaudioApp.Helpers;
using BaudioApp.Models;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BaudioApp.Services;

public enum HandoffCommand { EarbudsToPhone, EarbudsToPc }

/// The PC↔phone command channel: an RFCOMM server the Android companion app
/// connects to. Everything is event-driven (StreamSocketListener.ConnectionReceived
/// plus a per-connection read loop) — no polling — and messages are newline-delimited
/// JSON so the protocol stays trivial to implement on the Android side.
///
/// An inbound connection is itself the "phone is back in range" signal, which is why
/// the return-to-PC handoff can be immediate rather than waiting on the slow presence
/// reconnect loop.
public sealed class HandoffChannelService : IDisposable
{
    // Custom service UUID. The Android companion app must use the exact same value.
    private static readonly Guid HandoffServiceUuid = new("6f2b1a30-9c8d-4e7f-ab12-3c4d5e6f7a8b");

    private readonly DiagnosticService _diag;

    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private CancellationTokenSource? _readCts;

    public bool IsPhoneConnected => _socket is not null;

    public event Action? PhoneConnected;
    public event Action? PhoneDisconnected;
    public event Action<HandoffCommand>? CommandReceived;
    public event Action<EarbudLocation>? PhoneStatusReceived;

    public HandoffChannelService(DiagnosticService diag) => _diag = diag;

    /// Begin advertising the RFCOMM service and listening for the companion app.
    /// Safe to call when Bluetooth is off — it logs and stays idle.
    public async Task StartAsync()
    {
        try
        {
            _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(HandoffServiceUuid));

            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += OnConnectionReceived;
            await _listener.BindServiceNameAsync(
                _provider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            InitializeSdpAttributes(_provider);
            _provider.StartAdvertising(_listener, radioDiscoverable: true);

            _diag.Info("Handoff channel listening for phone", tags: ["Handoff"]);
        }
        catch (Exception ex)
        {
            _diag.Warn($"Could not start handoff channel: {ex.Message}", tags: ["Handoff"]);
        }
    }

    public void Stop()
    {
        _readCts?.Cancel();
        DropConnection();

        try { _provider?.StopAdvertising(); } catch { /* radio gone */ }
        _listener?.Dispose();
        _listener = null;
        _provider = null;
    }

    public Task SendCommandAsync(HandoffCommand cmd) => SendMessageAsync(new HandoffMessage
    {
        Type = cmd == HandoffCommand.EarbudsToPhone ? MsgEarbudsToPhone : MsgEarbudsToPc
    });

    public Task SendHelloAsync(EarbudMode mode, EarbudLocation state) => SendMessageAsync(new HandoffMessage
    {
        Type = MsgHello,
        Mode = mode.ToString(),
        EarbudState = state.ToString()
    });

    // ── Connection lifecycle ───────────────────────────────────────────────────

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        // Replace any stale connection (the phone may reconnect after a range trip).
        DropConnection();

        _socket = args.Socket;
        _writer = new DataWriter(_socket.OutputStream);
        _readCts = new CancellationTokenSource();

        _diag.Success("Phone connected to handoff channel", tags: ["Handoff"]);
        Ui.Post(() => PhoneConnected?.Invoke());

        _ = ReceiveLoopAsync(_socket, _readCts.Token);
    }

    private async Task ReceiveLoopAsync(StreamSocket socket, CancellationToken token)
    {
        var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        var line = new StringBuilder();
        try
        {
            while (!token.IsCancellationRequested)
            {
                uint count = await reader.LoadAsync(256).AsTask(token);
                if (count == 0) break;   // peer closed the socket

                for (uint i = 0; i < count; i++)
                {
                    char c = (char)reader.ReadByte();
                    if (c == '\n') { HandleLine(line.ToString()); line.Clear(); }
                    else if (c != '\r') line.Append(c);
                }
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
        catch (Exception ex)
        {
            _diag.Info($"Handoff channel read ended: {ex.Message}", tags: ["Handoff"]);
        }
        finally
        {
            reader.Dispose();
            if (socket == _socket)
            {
                DropConnection();
                _diag.Info("Phone disconnected from handoff channel", tags: ["Handoff"]);
                Ui.Post(() => PhoneDisconnected?.Invoke());
            }
        }
    }

    private void HandleLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        HandoffMessage? msg;
        try { msg = JsonSerializer.Deserialize<HandoffMessage>(json); }
        catch { _diag.Warn($"Ignoring malformed handoff message: {json}", tags: ["Handoff"]); return; }
        if (msg is null) return;

        switch (msg.Type)
        {
            case MsgEarbudsToPhone:
                Ui.Post(() => CommandReceived?.Invoke(HandoffCommand.EarbudsToPhone));
                break;
            case MsgEarbudsToPc:
                Ui.Post(() => CommandReceived?.Invoke(HandoffCommand.EarbudsToPc));
                break;
            case MsgPhoneStatus:
                if (Enum.TryParse<EarbudLocation>(msg.EarbudState, out var loc))
                    Ui.Post(() => PhoneStatusReceived?.Invoke(loc));
                break;
            case MsgHello:
                _diag.Info($"Phone hello (mode={msg.Mode}, earbuds={msg.EarbudState})", tags: ["Handoff"]);
                break;
        }
    }

    private async Task SendMessageAsync(HandoffMessage msg)
    {
        var writer = _writer;
        if (writer is null)
        {
            _diag.Warn("No phone connected — handoff command not delivered", tags: ["Handoff"]);
            return;
        }

        try
        {
            writer.WriteString(JsonSerializer.Serialize(msg) + "\n");
            await writer.StoreAsync();
            _diag.Info($"Sent to phone: {msg.Type}", tags: ["Handoff"]);
        }
        catch (Exception ex)
        {
            _diag.Warn($"Failed to send handoff command: {ex.Message}", tags: ["Handoff"]);
        }
    }

    private void DropConnection()
    {
        _writer?.Dispose();
        _writer = null;
        _socket?.Dispose();
        _socket = null;
    }

    private static void InitializeSdpAttributes(RfcommServiceProvider provider)
    {
        // Minimal service-name SDP attribute so the phone can recognise the endpoint.
        const uint serviceNameAttributeId = 0x100;
        const byte serviceNameAttributeType = (4 << 3) | 5; // UTF-8 string, 1-byte length

        const string serviceName = "BlueAmp Handoff";
        var writer = new DataWriter { UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8 };
        writer.WriteByte(serviceNameAttributeType);
        writer.WriteByte((byte)serviceName.Length);
        writer.WriteString(serviceName);
        provider.SdpRawAttributes.Add(serviceNameAttributeId, writer.DetachBuffer());
    }

    public void Dispose() => Stop();

    // ── Wire protocol ──────────────────────────────────────────────────────────

    private const string MsgHello          = "HELLO";
    private const string MsgEarbudsToPhone  = "EARBUDS_TO_PHONE";
    private const string MsgEarbudsToPc     = "EARBUDS_TO_PC";
    private const string MsgPhoneStatus     = "PHONE_STATUS";

    private sealed class HandoffMessage
    {
        [JsonPropertyName("type")]        public string Type { get; set; } = "";
        [JsonPropertyName("mode")]        public string? Mode { get; set; }
        [JsonPropertyName("earbudState")] public string? EarbudState { get; set; }
    }
}
