using System.IO;
using System.Text.Json;
using BaudioApp.Models;
using Microsoft.Win32;

namespace BaudioApp.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueAmp", "settings.json");

    private const string StartupRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegValue = "BlueAmp";

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch { /* corrupt settings — use defaults */ }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
        ApplyStartupRegistry();
    }

    // Call on every app launch to keep the registry entry in sync with the current exe path.
    public void ApplyStartup() => ApplyStartupRegistry();

    private void ApplyStartupRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, writable: true);
        if (key is null) return;

        if (Current.LaunchAtStartup)
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key.SetValue(StartupRegValue, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(StartupRegValue, throwOnMissingValue: false);
        }
    }
}
