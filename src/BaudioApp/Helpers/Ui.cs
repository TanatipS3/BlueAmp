using System.Windows.Forms;

namespace BaudioApp.Helpers;

/// Marshals work onto the WinForms UI thread — the replacement for WPF's
/// App.Current.Dispatcher. Backed by a hidden control whose handle lives on the
/// UI thread. Initialize() must be called once, on the UI thread, at startup.
internal static class Ui
{
    private static Control? _marshal;

    public static void Initialize(Control marshal) => _marshal = marshal;

    /// Fire-and-forget marshal (≈ Dispatcher.InvokeAsync). Runs inline if already
    /// on the UI thread or before init.
    public static void Post(Action action)
    {
        var m = _marshal;
        if (m is null || !m.InvokeRequired) { action(); return; }
        try { m.BeginInvoke(action); } catch { /* handle gone during shutdown */ }
    }

    /// Blocking marshal (≈ Dispatcher.Invoke). Runs inline if already on the UI thread.
    public static void Send(Action action)
    {
        var m = _marshal;
        if (m is null || !m.InvokeRequired) { action(); return; }
        try { m.Invoke(action); } catch { /* handle gone during shutdown */ }
    }
}
