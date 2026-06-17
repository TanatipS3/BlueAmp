using System.IO;

namespace BaudioApp.Helpers;

internal static class ShortcutHelper
{
    public static void EnsureDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var lnkPath = Path.Combine(desktop, "BlueAmp.lnk");
            if (File.Exists(lnkPath)) return;

            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell not available");

            dynamic shell    = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath       = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
            shortcut.Description      = "BlueAmp — Bluetooth Audio Receiver";
            shortcut.IconLocation     = exePath + ",0"; // use the icon embedded in the exe
            shortcut.Save();
        }
        catch { /* never block startup over a missing shortcut */ }
    }
}
