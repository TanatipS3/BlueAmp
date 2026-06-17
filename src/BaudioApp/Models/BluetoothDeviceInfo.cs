using CommunityToolkit.Mvvm.ComponentModel;

namespace BaudioApp.Models;

public enum DeviceConnectionState
{
    Available,
    Connecting,
    Connected,
    Error
}

public class BluetoothDeviceInfo : ObservableObject
{
    private DeviceConnectionState _state = DeviceConnectionState.Available;
    private bool _isAudioActive;
    private string? _errorMessage;
    private int? _signalStrength;

    public required string Id { get; init; }
    public required string Name { get; init; }

    public DeviceConnectionState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public bool IsAudioActive
    {
        get => _isAudioActive;
        set => SetProperty(ref _isAudioActive, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    // Signal strength percentage 0–100 from System.Devices.Aep.SignalStrength
    public int? SignalStrength
    {
        get => _signalStrength;
        set => SetProperty(ref _signalStrength, value);
    }
}
