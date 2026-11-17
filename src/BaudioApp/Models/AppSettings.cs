namespace BaudioApp.Models;

public class AppSettings
{
    public bool LaunchAtStartup { get; set; } = true;
    public bool RememberLastDevice { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool KeepLogHistory { get; set; } = false;
    public string? LastDeviceId { get; set; }
}
