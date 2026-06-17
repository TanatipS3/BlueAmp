using BaudioApp.Interop;

namespace BaudioApp.Helpers;

/// After AudioPlaybackConnection.OpenAsync() succeeds, the phone won't start
/// streaming until it receives an AVRCP volume command from the PC. This class
/// sends a 1% invisible volume nudge to trigger that handshake.
internal static class AudioStreamActivator
{
    public static async Task ActivateAsync(string deviceFriendlyName, CancellationToken ct = default)
    {
        // The audio endpoint may take a moment to register after OpenAsync() completes
        AudioEndpoint? endpoint = null;
        for (int i = 0; i < 8 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(400, ct).ConfigureAwait(false);
            endpoint = AudioDevices.FindActiveRenderByName(deviceFriendlyName);
            if (endpoint is not null) break;
        }

        if (endpoint is null || ct.IsCancellationRequested) { endpoint?.Dispose(); return; }

        try
        {
            if (!endpoint.TryGetVolumeScalar(out float vol)) return;

            // Unmute if muted (can happen on first-ever connection to a device)
            if (endpoint.IsMuted)
                endpoint.SetMute(false);

            // Send a 1% volume dip — invisible to the user, triggers AVRCP
            // SET_ABSOLUTE_VOLUME, which causes the phone to begin streaming
            endpoint.SetVolumeScalar(Math.Max(0f, vol - 0.01f));
            await Task.Delay(200, ct).ConfigureAwait(false);
            endpoint.SetVolumeScalar(vol);
        }
        catch { /* best-effort — never fail the connection over this */ }
        finally { endpoint.Dispose(); }
    }
}
