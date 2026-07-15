using BaudioApp.Helpers;
using BaudioApp.Interop;
using BaudioApp.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BaudioApp.Services;

// This file also contains the Phase 2 automatic-handoff policy. The walk-away and
// return signals both come from the phone's A2DP link state (surfaced by
// AudioConnectionService.StateChanged): Error = phone gone, Connected = phone back.
// Only single-point earbuds need active claim/release here — multipoint firmware
// re-routes audio on its own once the phone's A2DP link drops or returns.

/// Where the earbuds currently are, from the PC's point of view.
///  - OnPc:    connected to this PC (AudioSink service active).
///  - OnPhone: we released them to the phone (or the phone claimed them).
///  - Free:    not connected to the PC and not known to be on the phone (idle).
///  - Unknown: no earbud device configured, or state not yet resolved.
public enum EarbudLocation { Unknown, OnPc, OnPhone, Free }

/// One paired classic-Bluetooth audio device, offered in the Settings picker.
public sealed record EarbudDeviceOption(string Id, string Name, ulong Address);

/// Owns the user's earbud selection and the claim (connect-to-PC) / release
/// (disconnect-from-PC) operations used during handoff. In multipoint mode these
/// are only used for manual switching; in single-point mode they're the core of
/// the automatic handoff dance.
///
/// The actual driver calls (BluetoothClassic.SetAudioSinkConnection) block for
/// 1–3 s, so they always run on a background task; state is reported back through
/// Ui.Post, matching the app's marshaling convention.
public sealed class EarbudRouterService : IDisposable
{
    // Minimum time after any switch before an automatic switch may reverse it —
    // stops the earbuds flapping when you linger at the edge of PC range.
    private static readonly TimeSpan HandoffDwell = TimeSpan.FromSeconds(30);

    private readonly DiagnosticService _diag;
    private readonly SettingsService _settings;
    private readonly AudioConnectionService _audio;
    private readonly HandoffChannelService _channel;

    private BluetoothDevice? _earbud;   // WinRT handle, kept alive to receive status changes
    private ulong _earbudAddress;
    private EarbudLocation _location = EarbudLocation.Unknown;
    private DateTime _lastSwitchUtc = DateTime.MinValue;

    public EarbudLocation Location => _location;
    public string? EarbudName => _settings.Current.EarbudDeviceName;
    public bool HasEarbud => !string.IsNullOrEmpty(_settings.Current.EarbudDeviceId);

    /// Raised (on the UI thread) whenever the earbuds' location changes.
    public event Action<EarbudLocation>? LocationChanged;

    public EarbudRouterService(DiagnosticService diag, SettingsService settings,
                               AudioConnectionService audio, HandoffChannelService channel)
    {
        _diag = diag;
        _settings = settings;
        _audio = audio;
        _channel = channel;

        _audio.StateChanged        += OnPhoneAudioStateChanged;
        _channel.CommandReceived   += OnPhoneCommand;
        _channel.PhoneConnected    += OnPhoneChannelConnected;
        _channel.PhoneStatusReceived += OnPhoneStatus;
    }

    /// Resolve the configured earbud device, start tracking its link state, and
    /// recover from an unclean shutdown that may have left the AudioSink service
    /// disabled. Call once at startup (after Ui.Initialize).
    public async Task InitializeAsync()
    {
        await ReloadConfiguredDeviceAsync();

        if (_settings.Current.EarbudServicePendingReenable && _earbudAddress != 0)
        {
            _diag.Warn("Re-enabling earbud audio service after unclean shutdown", tags: ["Handoff"]);
            await RunServiceStateAsync(connect: true);
        }
        // Clear the flag regardless — if we can't resolve the device there's nothing
        // to re-enable, and leaving it set would retry forever.
        if (_settings.Current.EarbudServicePendingReenable)
        {
            _settings.Current.EarbudServicePendingReenable = false;
            _settings.Save();
        }
    }

    /// Enumerate paired classic-Bluetooth audio devices for the Settings picker.
    /// The default watcher in BluetoothService only surfaces A2DP *sources* (phones);
    /// earbuds are A2DP *sinks*, so they need this separate enumeration path.
    public async Task<IReadOnlyList<EarbudDeviceOption>> EnumeratePairedAudioDevicesAsync()
    {
        var results = new List<EarbudDeviceOption>();
        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var di in devices)
            {
                try
                {
                    using var bt = await BluetoothDevice.FromIdAsync(di.Id);
                    if (bt is null) continue;
                    if (bt.ClassOfDevice.MajorClass != BluetoothMajorClass.AudioVideo) continue;
                    results.Add(new EarbudDeviceOption(di.Id, di.Name, bt.BluetoothAddress));
                }
                catch { /* skip devices we can't inspect */ }
            }
        }
        catch (Exception ex)
        {
            _diag.Warn($"Could not enumerate paired audio devices: {ex.Message}", tags: ["Handoff"]);
        }
        return results;
    }

    /// Re-resolve the configured earbud from settings and (re)attach status tracking.
    /// Call after the Settings device selection is saved.
    public async Task ReloadConfiguredDeviceAsync()
    {
        DetachEarbud();

        var id = _settings.Current.EarbudDeviceId;
        if (string.IsNullOrEmpty(id)) { SetLocation(EarbudLocation.Unknown); return; }

        try
        {
            var bt = await BluetoothDevice.FromIdAsync(id);
            if (bt is null) { SetLocation(EarbudLocation.Unknown); return; }

            _earbud = bt;
            _earbudAddress = bt.BluetoothAddress;
            bt.ConnectionStatusChanged += OnConnectionStatusChanged;
            SetLocation(bt.ConnectionStatus == BluetoothConnectionStatus.Connected
                ? EarbudLocation.OnPc : EarbudLocation.Free);
        }
        catch (Exception ex)
        {
            _diag.Warn($"Could not resolve earbud device: {ex.Message}", tags: ["Handoff"]);
            SetLocation(EarbudLocation.Unknown);
        }
    }

    /// Connect the earbuds to this PC. Tells the phone to release them first, then
    /// claims them locally.
    public Task BringToPcAsync() => SwitchToPcAsync(notifyPhone: true);

    /// Release the earbuds from this PC so they're free to attach to the phone, then
    /// tells the phone to claim them.
    public Task SendToPhoneAsync() => SwitchToPhoneAsync(notifyPhone: true);

    private async Task SwitchToPhoneAsync(bool notifyPhone)
    {
        await RunServiceStateAsync(connect: false);
        if (notifyPhone) await _channel.SendCommandAsync(HandoffCommand.EarbudsToPhone);
    }

    private async Task SwitchToPcAsync(bool notifyPhone)
    {
        if (notifyPhone) await _channel.SendCommandAsync(HandoffCommand.EarbudsToPc);
        await RunServiceStateAsync(connect: true);
    }

    private async Task RunServiceStateAsync(bool connect)
    {
        if (_earbudAddress == 0)
        {
            _diag.Warn("No earbud device configured for handoff", tags: ["Handoff"]);
            return;
        }

        ulong addr = _earbudAddress;
        string name = _settings.Current.EarbudDeviceName ?? "earbuds";

        // Mark a disable as in-flight so a crash mid-release re-enables on next launch
        // (BluetoothSetServiceState persists the driver state).
        if (!connect)
        {
            _settings.Current.EarbudServicePendingReenable = true;
            _settings.Save();
        }

        _diag.Info($"{(connect ? "Connecting" : "Releasing")} {name}…", tags: ["Handoff"]);

        bool ok = await Task.Run(() => BluetoothClassic.SetAudioSinkConnection(addr, connect));

        if (!connect)
        {
            _settings.Current.EarbudServicePendingReenable = false;
            _settings.Save();
        }

        Ui.Post(() =>
        {
            if (ok)
            {
                _diag.Success($"{name} {(connect ? "connected to PC" : "released to phone")}", tags: ["Handoff"]);
                _lastSwitchUtc = DateTime.UtcNow;
                SetLocation(connect ? EarbudLocation.OnPc : EarbudLocation.OnPhone);
            }
            else
            {
                _diag.Error($"Failed to {(connect ? "connect" : "release")} {name}", null, "Handoff", "Error");
            }
        });
    }

    // ── Automatic handoff (Phase 2) ────────────────────────────────────────────

    private void OnPhoneAudioStateChanged(DeviceConnectionState state, string? error)
    {
        if (!_settings.Current.AutoHandoff) return;
        if (!HasEarbud) return;
        // Multipoint earbuds re-route in firmware; only single-point needs the dance.
        if (_settings.Current.EarbudMode != EarbudMode.SinglePoint) return;
        if (WithinDwell()) return;

        switch (state)
        {
            case DeviceConnectionState.Error:
                // Phone A2DP link lost and didn't recover → you walked away. Free the
                // earbuds so the phone can claim them (no-op if they're already gone).
                if (_location == EarbudLocation.OnPhone) return;
                _diag.Info("Phone link lost — releasing earbuds for handoff", tags: ["Handoff"]);
                _ = SendToPhoneAsync();
                break;

            case DeviceConnectionState.Connected:
                // Phone A2DP re-established → you returned. Reclaim the earbuds, but
                // only if we're the ones who handed them to the phone.
                if (_location != EarbudLocation.OnPhone) return;
                _diag.Info("Phone returned — reclaiming earbuds to PC", tags: ["Handoff"]);
                _ = BringToPcAsync();
                break;
        }
    }

    private bool WithinDwell() => DateTime.UtcNow - _lastSwitchUtc < HandoffDwell;

    // ── Handoff channel (Phase 3) ──────────────────────────────────────────────

    // A command the phone initiated (its QS tile / notification). We perform the
    // local half only — the phone is already handling its own side, so passing
    // notifyPhone:false avoids echoing the command back and looping.
    private void OnPhoneCommand(HandoffCommand cmd)
    {
        switch (cmd)
        {
            case HandoffCommand.EarbudsToPc:    _ = SwitchToPcAsync(notifyPhone: false); break;
            case HandoffCommand.EarbudsToPhone: _ = SwitchToPhoneAsync(notifyPhone: false); break;
        }
    }

    // An inbound channel connection means the phone is back in range — a faster,
    // more reliable return signal than the A2DP presence loop.
    private void OnPhoneChannelConnected()
    {
        _ = _channel.SendHelloAsync(_settings.Current.EarbudMode, _location);

        if (!_settings.Current.AutoHandoff) return;
        if (_settings.Current.EarbudMode != EarbudMode.SinglePoint) return;
        if (_location != EarbudLocation.OnPhone) return;
        if (WithinDwell()) return;

        _diag.Info("Phone back in range (channel) — reclaiming earbuds", tags: ["Handoff"]);
        _ = SwitchToPcAsync(notifyPhone: true);
    }

    // The phone tells us where the earbuds are. Only trust it when they aren't on us
    // (the PC's own link state is authoritative for OnPc).
    private void OnPhoneStatus(EarbudLocation reported)
    {
        if (_location != EarbudLocation.OnPc)
            SetLocation(reported);
    }

    private void OnConnectionStatusChanged(BluetoothDevice sender, object args)
    {
        var status = sender.ConnectionStatus;
        Ui.Post(() =>
        {
            if (status == BluetoothConnectionStatus.Connected)
            {
                SetLocation(EarbudLocation.OnPc);
            }
            else if (_location == EarbudLocation.OnPc)
            {
                // Dropped the PC link without us releasing it — treat as idle. A later
                // release (or RFCOMM phone status) refines this to OnPhone.
                SetLocation(EarbudLocation.Free);
            }
        });
    }

    private void SetLocation(EarbudLocation loc)
    {
        if (_location == loc) return;
        _location = loc;
        LocationChanged?.Invoke(loc);
    }

    private void DetachEarbud()
    {
        if (_earbud is not null)
        {
            _earbud.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _earbud.Dispose();
            _earbud = null;
        }
        _earbudAddress = 0;
    }

    public void Dispose()
    {
        _audio.StateChanged          -= OnPhoneAudioStateChanged;
        _channel.CommandReceived     -= OnPhoneCommand;
        _channel.PhoneConnected      -= OnPhoneChannelConnected;
        _channel.PhoneStatusReceived -= OnPhoneStatus;
        DetachEarbud();
    }
}
