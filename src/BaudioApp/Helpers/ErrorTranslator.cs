using Windows.Media.Audio;

namespace BaudioApp.Helpers;

public static class ErrorTranslator
{
    public static string Translate(AudioPlaybackConnectionOpenResultStatus status) => status switch
    {
        AudioPlaybackConnectionOpenResultStatus.Success => "Connected successfully.",
        AudioPlaybackConnectionOpenResultStatus.RequestTimedOut =>
            "Connection timed out. Make sure your phone's Bluetooth is on and the device is nearby.",
        AudioPlaybackConnectionOpenResultStatus.DeniedBySystem =>
            "Windows denied the connection. Check Bluetooth permissions in Settings → Privacy → Other devices.",
        AudioPlaybackConnectionOpenResultStatus.UnknownFailure =>
            "Connection failed for an unknown reason. Try disconnecting and reconnecting the device in Windows Bluetooth settings.",
        _ => "Unexpected error. Restart BlueAmp and try again."
    };
}
