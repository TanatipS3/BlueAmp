using System.Runtime.InteropServices;

namespace BaudioApp.Interop;

/// A render audio endpoint. Wraps an IMMDevice and lazily activates the meter
/// and volume sub-interfaces on first use. Dispose to release the COM objects.
internal sealed class AudioEndpoint : IDisposable
{
    private const uint ClsCtxAll = 0x17;
    private const uint StgmRead  = 0x0;

    // PKEY_Device_FriendlyName
    private static readonly PropertyKey PkeyFriendlyName = new()
    {
        FmtId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        Pid   = 14
    };

    private static Guid _meterIid  = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    private static Guid _volumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private IMMDevice _device;
    private IAudioMeterInformation? _meter;
    private IAudioEndpointVolume? _volume;

    public AudioEndpoint(IMMDevice device) => _device = device;

    public string? FriendlyName
    {
        get
        {
            if (_device.OpenPropertyStore(StgmRead, out var store) != 0 || store is null)
                return null;
            try
            {
                var key = PkeyFriendlyName;
                if (store.GetValue(ref key, out var pv) != 0) return null;
                try { return pv.AsString; }
                finally { Ole32.PropVariantClear(ref pv); }
            }
            finally { Marshal.ReleaseComObject(store); }
        }
    }

    public float PeakValue
    {
        get
        {
            var meter = Meter;
            if (meter is null) return 0f;
            return meter.GetPeakValue(out float peak) == 0 ? peak : 0f;
        }
    }

    public bool TryGetVolumeScalar(out float level)
    {
        level = 0f;
        var vol = Volume;
        return vol is not null && vol.GetMasterVolumeLevelScalar(out level) == 0;
    }

    public void SetVolumeScalar(float level)
    {
        var ctx = Guid.Empty;
        Volume?.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref ctx);
    }

    public bool IsMuted
    {
        get
        {
            var vol = Volume;
            return vol is not null && vol.GetMute(out bool mute) == 0 && mute;
        }
    }

    public void SetMute(bool mute)
    {
        var ctx = Guid.Empty;
        Volume?.SetMute(mute, ref ctx);
    }

    private IAudioMeterInformation? Meter
    {
        get
        {
            if (_meter is not null) return _meter;
            if (_device.Activate(ref _meterIid, ClsCtxAll, IntPtr.Zero, out object o) == 0)
                _meter = (IAudioMeterInformation)o;
            return _meter;
        }
    }

    private IAudioEndpointVolume? Volume
    {
        get
        {
            if (_volume is not null) return _volume;
            if (_device.Activate(ref _volumeIid, ClsCtxAll, IntPtr.Zero, out object o) == 0)
                _volume = (IAudioEndpointVolume)o;
            return _volume;
        }
    }

    public void Dispose()
    {
        if (_meter  is not null) { Marshal.ReleaseComObject(_meter);  _meter  = null; }
        if (_volume is not null) { Marshal.ReleaseComObject(_volume); _volume = null; }
        if (_device is not null) { Marshal.ReleaseComObject(_device); _device = null!; }
    }
}

/// Entry point for enumerating render endpoints — the NAudio MMDeviceEnumerator replacement.
internal static class AudioDevices
{
    public static AudioEndpoint? GetDefaultRender()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var dev) == 0 && dev is not null)
                return new AudioEndpoint(dev);
            return null;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    /// Returns the first active render endpoint whose friendly name contains <paramref name="namePart"/>.
    /// All non-matching endpoints are released immediately so nothing leaks.
    public static AudioEndpoint? FindActiveRenderByName(string namePart)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var collection) != 0 || collection is null)
                return null;
            try
            {
                if (collection.GetCount(out uint count) != 0) return null;
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var dev) != 0 || dev is null) continue;
                    var endpoint = new AudioEndpoint(dev);
                    var name = endpoint.FriendlyName;
                    if (name is not null && name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                        return endpoint;
                    endpoint.Dispose();
                }
                return null;
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
}
