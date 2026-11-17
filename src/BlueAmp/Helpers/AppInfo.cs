using System.IO;
using System.Reflection;

namespace BlueAmp.Helpers;

/// Identifies the running build: the assembly version plus when the executable was
/// built. Local builds all share one version number, so the build time is what
/// actually tells two of them apart.
internal static class AppInfo
{
    public static readonly string Version = ReadVersion();
    public static readonly string VersionLabel = BuildLabel();

    private static string ReadVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static string BuildLabel()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return $"v{Version} · built {File.GetLastWriteTime(path):MMM d, HH:mm}";
        }
        catch { /* fall through to the bare version */ }
        return $"v{Version}";
    }
}
