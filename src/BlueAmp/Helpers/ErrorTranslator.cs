using Windows.Media.Audio;

namespace BlueAmp.Helpers;

public static class ErrorTranslator
{
    public static string Translate(AudioPlaybackConnectionOpenResultStatus status) => Translate(status, 0);

    /// Turns a failed connect into something actionable. Windows often reports only
    /// "UnknownFailure" as the status, but hands back a specific error code next to
    /// it — that code is what distinguishes "phone out of range" from "phone is busy".
    public static string Translate(AudioPlaybackConnectionOpenResultStatus status, int hresult)
    {
        if (status == AudioPlaybackConnectionOpenResultStatus.Success)
            return "Connected successfully.";

        if (ExplainHResult(hresult) is { } specific)
            return specific;

        string message = status switch
        {
            AudioPlaybackConnectionOpenResultStatus.RequestTimedOut =>
                "Connection timed out. Make sure your phone's Bluetooth is on and the device is nearby.",
            AudioPlaybackConnectionOpenResultStatus.DeniedBySystem =>
                "Windows denied the connection. Check Bluetooth permissions in Settings → Privacy → Other devices.",
            AudioPlaybackConnectionOpenResultStatus.UnknownFailure =>
                "Windows couldn't open the audio connection. Check that the phone's Bluetooth is on, that it isn't " +
                "playing to another device, and that \"Media audio\" is enabled for this PC in the phone's Bluetooth settings.",
            _ => "Unexpected error. Restart BlueAmp and try again."
        };

        // Always show the code when there is one, so "unknown" is at least searchable.
        return hresult != 0 ? $"{message} (error {FormatHResult(hresult)})" : message;
    }

    public static string FormatHResult(int hresult) => $"0x{hresult:X8}";

    private static string? ExplainHResult(int hresult) => unchecked((uint)hresult) switch
    {
        0x8007048F => // ERROR_DEVICE_NOT_CONNECTED
            "The phone isn't reachable. Check that its Bluetooth is on and that it's within range.",
        0x800705B4 or 0x8007274C => // ERROR_TIMEOUT / WSAETIMEDOUT
            "The phone didn't answer in time. Make sure its Bluetooth is on and it's nearby, then try again.",
        0x80070005 => // E_ACCESSDENIED
            "Windows blocked the connection. Check Bluetooth permissions in Settings → Privacy → Other devices.",
        0x80070490 => // ERROR_NOT_FOUND
            "Windows no longer knows this device. Pair the phone with this PC again in Bluetooth settings.",
        0x8007001F => // ERROR_GEN_FAILURE
            "The PC's Bluetooth adapter reported a failure. Turn Bluetooth off and on again in Windows, then retry.",
        0x800710DF => // ERROR_DEVICE_NOT_AVAILABLE
            "The PC's Bluetooth is off or unavailable. Turn Bluetooth on in Windows and try again.",
        0x8007045D => // ERROR_IO_DEVICE
            "The Bluetooth link failed while connecting. Move the phone closer and try again.",
        _ => null
    };
}
