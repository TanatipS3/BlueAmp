using System.IO;
using System.Text.Json;
using BlueAmp.Models;
using Microsoft.Win32;

namespace BlueAmp.Services;

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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });

            // Write to a side file and swap it in, so a crash or power loss mid-save
            // can never leave a half-written settings.json (which would silently reset
            // every setting to its default).
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disk full / file locked — keep running with the in-memory settings.
        }
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
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            key.SetValue(StartupRegValue, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(StartupRegValue, throwOnMissingValue: false);
        }
    }
}
