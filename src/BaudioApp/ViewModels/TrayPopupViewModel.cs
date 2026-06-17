using System.Collections.ObjectModel;
using BaudioApp.Helpers;
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
        private set
        {
            var previous = _connectedDevice;
            if (SetProperty(ref _connectedDevice, value))
            {
                // Detach the handler from the old device so they don't accumulate
                // across reconnects (each one would otherwise leak a closure).
                if (previous is not null)
                    previous.PropertyChanged -= OnConnectedDevicePropertyChanged;
                if (value is not null)
                    value.PropertyChanged += OnConnectedDevicePropertyChanged;
                OnPropertyChanged(nameof(SignalBars));
            }
        }
    }

    private void OnConnectedDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BluetoothDeviceInfo.SignalStrength))
            OnPropertyChanged(nameof(SignalBars));
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

    public string Codec   => "SBC";
    public string Bitrate => "328 kbps";

    public int SignalBars
    {
        get
        {
            var sig = ConnectedDevice?.SignalStrength;
            if (sig is null) return 4;
            return sig >= 75 ? 4 : sig >= 50 ? 3 : sig >= 25 ? 2 : 1;
        }
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
        App.Current.Dispatcher.InvokeAsync(() =>
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
        App.Current.Dispatcher.InvokeAsync(() =>
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
}
