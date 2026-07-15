namespace BaudioApp.Models;

/// How the earbuds relate to the two audio sources (PC and phone).
///  - Multipoint: the buds hold both links at once and switch audio in firmware.
///    BlueAmp only manages the phone→PC A2DP link; it never claims/releases the buds.
///  - SinglePoint: the buds hold one link at a time. BlueAmp performs the full
///    claim (connect to PC) / release (disconnect from PC) dance during handoff.
public enum EarbudMode
{
    Multipoint,
    SinglePoint
}

public class AppSettings
{
    public bool LaunchAtStartup { get; set; } = true;
    public bool RememberLastDevice { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool KeepLogHistory { get; set; } = false;
    public bool PresenceDetection { get; set; } = false;
    public string? LastDeviceId { get; set; }

    // ── Earbud handoff ────────────────────────────────────────────────────────
    public string? EarbudDeviceId { get; set; }
    public string? EarbudDeviceName { get; set; }
    public EarbudMode EarbudMode { get; set; } = EarbudMode.Multipoint;
    public bool AutoHandoff { get; set; } = false;

    // Crash-safety flag. BluetoothSetServiceState persists the AudioSink service
    // state in the driver, so a crash mid-release could leave the earbuds unable
    // to reconnect to the PC. We set this true right before disabling the service
    // and clear it after re-enabling; on startup, if it's still set, we force the
    // service back on so the user's earbuds are never left stranded.
    public bool EarbudServicePendingReenable { get; set; } = false;
}
