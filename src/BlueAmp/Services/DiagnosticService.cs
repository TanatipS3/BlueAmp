using System.Collections.ObjectModel;
using BlueAmp.Helpers;
using BlueAmp.Models;

namespace BlueAmp.Services;

public class DiagnosticService
{
    // Hard cap so a long-running session can't grow the log unbounded.
    private const int MaxEntries = 500;

    public ObservableCollection<DiagnosticEntry> Entries { get; } = [];

    /// When false (the default), only Error entries are kept. Info/Success/Warning
    /// are dropped before any allocation or dispatcher marshaling, so routine
    /// operation costs nothing. Flip on from Settings to troubleshoot.
    public bool VerboseEnabled { get; set; }

    public void Log(DiagnosticLevel level, string message, string? detail = null, params string[] tags)
    {
        // Errors are always captured; everything else only when verbose logging is on.
        if (!VerboseEnabled && level != DiagnosticLevel.Error) return;

        var entry = new DiagnosticEntry { Level = level, Message = message, Detail = detail, Tags = tags };
        // InvokeAsync (non-blocking) so background threads — the BT watcher, audio
        // callbacks — never stall waiting on the UI thread just to log.
        Ui.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        });
    }

    public void Info(string message, string? detail = null, params string[] tags) =>
        Log(DiagnosticLevel.Info, message, detail, tags);

    public void Success(string message, string? detail = null, params string[] tags) =>
        Log(DiagnosticLevel.Success, message, detail, tags);

    public void Warn(string message, string? detail = null, params string[] tags) =>
        Log(DiagnosticLevel.Warning, message, detail, tags);

    public void Error(string message, string? detail = null, params string[] tags) =>
        Log(DiagnosticLevel.Error, message, detail, tags);

    public void Clear() => Ui.Send(Entries.Clear);
}
