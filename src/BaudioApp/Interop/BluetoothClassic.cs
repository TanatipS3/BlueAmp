using System.Runtime.InteropServices;

namespace BaudioApp.Interop;

/// Minimal interop over the classic Bluetooth management API exported from
/// bthprops.cpl. We use BluetoothSetServiceState to force a paired A2DP sink
/// (earbuds / headphones) to connect to, or disconnect from, this PC — the same
/// mechanism ToothTray and similar tray utilities rely on.
///
/// Disabling the AudioSink service disconnects the device; enabling it connects.
/// The call re-plumbs the Bluetooth driver and can take 1–3 s, so it must never
/// run on the UI thread.
internal static class BluetoothClassic
{
    // AudioSink (A2DP sink) service class — 0000110B-0000-1000-8000-00805F9B34FB.
    private static Guid AudioSinkService = new("0000110B-0000-1000-8000-00805F9B34FB");

    private const uint BLUETOOTH_SERVICE_DISABLE = 0x00;
    private const uint BLUETOOTH_SERVICE_ENABLE  = 0x01;
    private const int  BLUETOOTH_MAX_NAME_SIZE   = 248;
    private const uint ERROR_SUCCESS             = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;          // BLUETOOTH_ADDRESS union — low 48 bits hold the address
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("bthprops.cpl")]
    private static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl")]
    private static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi,
        ref Guid pGuidService, uint dwServiceFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// Connect (enable) or disconnect (disable) the AudioSink service on the paired
    /// device with the given 48-bit Bluetooth address (as returned by WinRT
    /// BluetoothDevice.BluetoothAddress). Returns true on success.
    ///
    /// Blocking; call only from a background thread. A PC may expose more than one
    /// Bluetooth radio, so we walk every radio and act on the first one that
    /// actually knows this device.
    public static bool SetAudioSinkConnection(ulong bluetoothAddress, bool connect)
    {
        var findParams = new BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        IntPtr hFind = BluetoothFindFirstRadio(ref findParams, out IntPtr hRadio);
        if (hFind == IntPtr.Zero) return false;

        uint flags = connect ? BLUETOOTH_SERVICE_ENABLE : BLUETOOTH_SERVICE_DISABLE;
        bool success = false;

        try
        {
            do
            {
                var info = new BLUETOOTH_DEVICE_INFO
                {
                    dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
                    Address = bluetoothAddress
                };

                // Only the radio that already knows this paired device can toggle it;
                // GetDeviceInfo fails with ERROR_NOT_FOUND on the others.
                if (BluetoothGetDeviceInfo(hRadio, ref info) == ERROR_SUCCESS)
                {
                    success = BluetoothSetServiceState(hRadio, ref info, ref AudioSinkService, flags) == ERROR_SUCCESS;
                    CloseHandle(hRadio);
                    if (success) break;
                }
                else
                {
                    CloseHandle(hRadio);
                }
            }
            while (BluetoothFindNextRadio(hFind, out hRadio));
        }
        finally
        {
            BluetoothFindRadioClose(hFind);
        }

        return success;
    }
}
