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

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return; // can't make a shortcut without a target

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            var shellObj = Activator.CreateInstance(shellType);
            if (shellObj is null) return;

            dynamic shell    = shellObj;
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
