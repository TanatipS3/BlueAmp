using System.IO;

namespace BaudioApp.Helpers;

/// Appends fatal errors to %APPDATA%\BlueAmp\crash.log. The in-memory diagnostics
/// log dies with the process, so without this a crash leaves nothing to look at.
internal static class CrashLog
{
    private const long MaxBytes = 256 * 1024;

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueAmp", "crash.log");

    public static void Write(string source, object? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Start over rather than grow forever if something crashes in a loop.
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > MaxBytes) info.Delete();

            File.AppendAllText(FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* never let crash reporting cause a crash of its own */ }
    }
}
