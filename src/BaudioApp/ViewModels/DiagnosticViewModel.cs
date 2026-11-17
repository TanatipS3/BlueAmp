using System.Collections.ObjectModel;
using BaudioApp.Models;
using BaudioApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaudioApp.ViewModels;

public sealed class DiagnosticViewModel : ObservableObject
{
    /// Filter names, in toolbar order. Each one matches entries that really exist:
    /// "Connection" = the phone's A2DP link, "System" = app and device-list events.
    public static readonly string[] Filters = ["All", "Connection", "System", "Errors"];

    private readonly DiagnosticService _diag;

    private string _activeFilter = "All";

    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value))
                OnPropertyChanged(nameof(FilteredEntries));
        }
    }

    public ObservableCollection<DiagnosticEntry> Entries => _diag.Entries;

    public IEnumerable<DiagnosticEntry> FilteredEntries => Entries.Where(Matches);

    /// Whether an entry passes the active filter.
    public bool Matches(DiagnosticEntry entry) => ActiveFilter switch
    {
        "Connection" => entry.Tags.Contains("A2DP"),
        "System"     => entry.Tags.Contains("System"),
        "Errors"     => entry.Level == DiagnosticLevel.Error,
        _            => true
    };

    public IRelayCommand ClearCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IRelayCommand<string> FilterCommand { get; }

    // Note: this view-model deliberately does not subscribe to the log itself. The
    // log outlives every Diagnostics window, so a subscription here would keep each
    // closed window's view-model alive forever. The form listens (and unsubscribes).
    public DiagnosticViewModel(DiagnosticService diag)
    {
        _diag = diag;
        ClearCommand  = new RelayCommand(_diag.Clear);
        CopyCommand   = new RelayCommand(CopyToClipboard);
        FilterCommand = new RelayCommand<string>(f => ActiveFilter = f ?? "All");
    }

    private void CopyToClipboard()
    {
        var lines = FilteredEntries.Select(e =>
        {
            var line = $"[{e.Timestamp:HH:mm:ss}] {e.Level,-7} {e.Message}";
            return e.Detail is { Length: > 0 } d ? line + "\n           " + d : line;
        }).ToList();
        if (lines.Count == 0) return;   // Clipboard.SetText throws on an empty string
        System.Windows.Forms.Clipboard.SetText(string.Join("\n", lines));
    }
}
