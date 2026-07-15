using BaudioApp.Models;
using BaudioApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaudioApp.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _svc;
    private readonly EarbudRouterService _earbuds;

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

    public bool PresenceDetection
    {
        get => _svc.Current.PresenceDetection;
        set { _svc.Current.PresenceDetection = value; OnPropertyChanged(); }
    }

    // ── Earbud handoff ────────────────────────────────────────────────────────

    /// Single-point earbuds hold only one link at a time, so BlueAmp must actively
    /// claim/release them. Multipoint (the default) leaves switching to firmware.
    public bool SinglePointEarbuds
    {
        get => _svc.Current.EarbudMode == EarbudMode.SinglePoint;
        set { _svc.Current.EarbudMode = value ? EarbudMode.SinglePoint : EarbudMode.Multipoint; OnPropertyChanged(); }
    }

    public bool AutoHandoff
    {
        get => _svc.Current.AutoHandoff;
        set { _svc.Current.AutoHandoff = value; OnPropertyChanged(); }
    }

    public string? SelectedEarbudId => _svc.Current.EarbudDeviceId;

    /// Live in-memory selection; persisted with the rest on Save.
    public void SetEarbud(EarbudDeviceOption? option)
    {
        _svc.Current.EarbudDeviceId = option?.Id;
        _svc.Current.EarbudDeviceName = option?.Name;
        OnPropertyChanged(nameof(SelectedEarbudId));
    }

    public Task<IReadOnlyList<EarbudDeviceOption>> LoadEarbudDevicesAsync() =>
        _earbuds.EnumeratePairedAudioDevicesAsync();

    public IRelayCommand SaveCommand { get; }

    public event Action? Saved;

    public SettingsViewModel(SettingsService svc, EarbudRouterService earbuds)
    {
        _svc = svc;
        _earbuds = earbuds;
        SaveCommand = new RelayCommand(() => { _svc.Save(); Saved?.Invoke(); });
    }
}
