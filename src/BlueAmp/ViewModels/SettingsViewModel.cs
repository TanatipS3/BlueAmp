using BlueAmp.Models;
using BlueAmp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlueAmp.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _svc;

    public bool LaunchAtStartup
    {
        get => _svc.Current.LaunchAtStartup;
        set { _svc.Current.LaunchAtStartup = value; OnPropertyChanged(); }
    }

    public bool RememberLastDevice
    {
        get => _svc.Current.RememberLastDevice;
        set { _svc.Current.RememberLastDevice = value; OnPropertyChanged(); }
    }

    public bool AutoReconnect
    {
        get => _svc.Current.AutoReconnect;
        set { _svc.Current.AutoReconnect = value; OnPropertyChanged(); }
    }

    public bool KeepLogHistory
    {
        get => _svc.Current.KeepLogHistory;
        set { _svc.Current.KeepLogHistory = value; OnPropertyChanged(); }
    }

    public IRelayCommand SaveCommand { get; }

    public event Action? Saved;

    public SettingsViewModel(SettingsService svc)
    {
        _svc = svc;
        SaveCommand = new RelayCommand(() => { _svc.Save(); Saved?.Invoke(); });
    }
}
