using System.Collections.ObjectModel;
using BaudioApp.Helpers;
using BaudioApp.Interop;
using BaudioApp.Models;
using BaudioApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaudioApp.ViewModels;

public sealed class TrayPopupViewModel : ObservableObject
{
    private readonly BluetoothService _bt;
    private readonly AudioConnectionService _audio;
    private readonly AudioMeterService _meter;

    private BluetoothDeviceInfo? _connectedDevice;
    private BluetoothDeviceInfo? _pendingDevice;   // device being connected to (may differ from connected)
    private DeviceConnectionState _overallState = DeviceConnectionState.Available;
    private string? _errorMessage;
    private float _audioPeak;
    private bool _popupVisible;

    private const int WaveformSlots = 24;

    public BluetoothDeviceInfo? ConnectedDevice
    {
        get => _connectedDevice;
        private set => SetProperty(ref _connectedDevice, value);
    }

    public DeviceConnectionState OverallState
    {
        get => _overallState;
        private set => SetProperty(ref _overallState, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public float AudioPeak
    {
        get => _audioPeak;
        private set => SetProperty(ref _audioPeak, value);
    }

    private string _sampleRateText = "—";
    private string _bitDepthText   = "—";

    /// Sample rate of the phone's stream when Windows exposes it, else of the
    /// PC output device it plays through (e.g. "48 kHz").
    public string SampleRateText
    {
        get => _sampleRateText;
        private set => SetProperty(ref _sampleRateText, value);
    }

    /// Real bit depth of the connected render endpoint (e.g. "16-bit").
    public string BitDepthText
    {
        get => _bitDepthText;
        private set => SetProperty(ref _bitDepthText, value);
    }

    private string _channelsText = "—";

    /// Channel layout of the stream (e.g. "Stereo").
    public string ChannelsText
    {
        get => _channelsText;
        private set => SetProperty(ref _channelsText, value);
    }

    public ObservableCollection<double> PeakHistory { get; } =
        new(Enumerable.Repeat(0.0, WaveformSlots));

    public ObservableCollection<BluetoothDeviceInfo> Devices => _bt.Devices;

    public IAsyncRelayCommand<BluetoothDeviceInfo> ConnectCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand DisconnectCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenDiagnosticsCommand { get; }

    public event Action? SettingsRequested;
    public event Action? DiagnosticsRequested;
    public event Action<DeviceConnectionState>? TrayIconStateChanged;

    public TrayPopupViewModel(BluetoothService bt, AudioConnectionService audio, AudioMeterService meter)
    {
        _bt = bt;
        _audio = audio;
        _meter = meter;

        ConnectCommand         = new AsyncRelayCommand<BluetoothDeviceInfo>(ConnectAsync);
        CancelCommand          = new RelayCommand(Cancel);
        DisconnectCommand      = new RelayCommand(Disconnect);
        OpenSettingsCommand    = new RelayCommand(() => SettingsRequested?.Invoke());
        OpenDiagnosticsCommand = new RelayCommand(() => DiagnosticsRequested?.Invoke());

        _audio.StateChanged += OnAudioStateChanged;
        _meter.PeakChanged  += OnPeakChanged;
    }

    private void OnPeakChanged(float peak)
    {
        Ui.Post(() =>
        {
            AudioPeak = peak;
            if (ConnectedDevice is not null)
                ConnectedDevice.IsAudioActive = peak > 0.01f;
            PeakHistory.RemoveAt(0);
            PeakHistory.Add(peak);
        });
    }

    /// Called by the view when the tray popup is shown/hidden. The waveform only
    /// matters while the popup is visible, so we run the 100 ms meter loop only
    /// then — when the popup is closed the metering timer is fully stopped.
    public void SetPopupVisible(bool visible)
    {
        _popupVisible = visible;
        UpdateMeterState();

        // The BT render endpoint can appear a moment after the connection opens,
        // so re-read the real format each time the popup is shown.
        if (visible && OverallState == DeviceConnectionState.Connected)
            RefreshEndpointFormat();

        // The popup just closed — a good moment to hand idle heap back to the OS.
        if (!visible)
            MemoryHelper.TrimNow();
    }

    private void UpdateMeterState()
    {
        if (_popupVisible && OverallState == DeviceConnectionState.Connected)
            _meter.Resume();
        else
            _meter.Pause();
    }

    private async Task ConnectAsync(BluetoothDeviceInfo? device)
    {
        if (device is null) return;
        if (OverallState == DeviceConnectionState.Connecting) return;
        if (device == ConnectedDevice && OverallState == DeviceConnectionState.Connected) return;

        _pendingDevice = device;
        await _audio.ConnectAsync(device);
    }

    private void Cancel() => _audio.Cancel();

    private void Disconnect() => _audio.Disconnect();

    private void OnAudioStateChanged(DeviceConnectionState state, string? error)
    {
        Ui.Post(() =>
        {
            OverallState = state;
            ErrorMessage = error;

            switch (state)
            {
                case DeviceConnectionState.Connecting:
                    // Show the pending device as connecting; leave the current connected device untouched
                    if (_pendingDevice is not null)
                        _pendingDevice.State = DeviceConnectionState.Connecting;
                    break;

                case DeviceConnectionState.Connected:
                    // AudioConnectionService.ActiveDevice tells us which device is now live
                    var newActive = _audio.ActiveDevice;

                    // Reset the old connected device if we're switching
                    if (ConnectedDevice is not null && ConnectedDevice != newActive)
                    {
                        ConnectedDevice.State = DeviceConnectionState.Available;
                        ConnectedDevice.IsAudioActive = false;
                    }

                    // Reset the pending device if it wasn't the one that ended up connected
                    // (happens on restore: B failed, A is restored)
                    if (_pendingDevice is not null && _pendingDevice != newActive)
                        _pendingDevice.State = DeviceConnectionState.Available;

                    ConnectedDevice = newActive;
                    if (ConnectedDevice is not null)
                        ConnectedDevice.State = DeviceConnectionState.Connected;
                    _pendingDevice = null;

                    _meter.Start(ConnectedDevice?.Name);
                    UpdateMeterState();   // resumes only if the popup is currently open
                    RefreshEndpointFormat();
                    break;

                case DeviceConnectionState.Error:
                    if (_pendingDevice is not null)
                    {
                        _pendingDevice.State = DeviceConnectionState.Error;
                        _pendingDevice.ErrorMessage = error;
                        _pendingDevice = null;
                    }
                    // If the active connection also died, clear it
                    if (_audio.ActiveDevice is null && ConnectedDevice is not null)
                    {
                        ConnectedDevice.State = DeviceConnectionState.Available;
                        ConnectedDevice.IsAudioActive = false;
                        ConnectedDevice = null;
                    }
                    _meter.Stop();
                    ResetWaveform();
                    break;

                case DeviceConnectionState.Available:
                    _meter.Stop();
                    if (ConnectedDevice is not null)
                    {
                        ConnectedDevice.State = DeviceConnectionState.Available;
                        ConnectedDevice.IsAudioActive = false;
                        ConnectedDevice = null;
                    }
                    if (_pendingDevice is not null)
                    {
                        _pendingDevice.State = DeviceConnectionState.Available;
                        _pendingDevice = null;
                    }
                    ResetWaveform();
                    break;
            }

            TrayIconStateChanged?.Invoke(state);
        });
    }

    private void ResetWaveform()
    {
        for (int i = 0; i < PeakHistory.Count; i++)
            PeakHistory[i] = 0.0;
    }

    /// Reads the actual shared-mode format from the device's render endpoint.
    /// Falls back to "—" whenever the endpoint (or the format) isn't available.
    private void RefreshEndpointFormat()
    {
        string sr = "—", bd = "—", ch = "—";
        if (ConnectedDevice?.Name is { } name)
        {
            // Best source is the phone's own incoming stream, which Windows exposes
            // as an endpoint named after the phone — that is the format the phone is
            // really sending. Otherwise fall back to the PC's output device, which
            // is where the audio ends up playing.
            using var endpoint = AudioDevices.FindPhoneStream(name)
                                 ?? AudioDevices.FindActiveRenderByName(name)
                                 ?? AudioDevices.GetDefaultRender();
            if (endpoint is not null && endpoint.TryGetDeviceFormat(out int hz, out int bits, out int channels))
            {
                sr = hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.#} kHz";
                bd = $"{bits}-bit";
                ch = channels switch { 1 => "Mono", 2 => "Stereo", _ => $"{channels} ch" };
            }
        }
        SampleRateText = sr;
        BitDepthText   = bd;
        ChannelsText   = ch;
    }
}
