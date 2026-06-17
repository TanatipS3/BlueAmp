using System.Collections.ObjectModel;
using BaudioApp.Models;
using BaudioApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaudioApp.ViewModels;

public sealed class DiagnosticViewModel : ObservableObject
{
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

    public IEnumerable<DiagnosticEntry> FilteredEntries => ActiveFilter switch
    {
        "Connection" => Entries.Where(e => e.Tags.Contains("A2DP")),
        "Audio"      => Entries.Where(e => e.Tags.Contains("Audio")),
        "Errors"     => Entries.Where(e => e.Level == DiagnosticLevel.Error),
        _            => Entries
    };

    public IRelayCommand ClearCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IRelayCommand<string> FilterCommand { get; }

    public DiagnosticViewModel(DiagnosticService diag)
    {
        _diag = diag;
        ClearCommand  = new RelayCommand(_diag.Clear);
        CopyCommand   = new RelayCommand(CopyToClipboard);
        FilterCommand = new RelayCommand<string>(f => ActiveFilter = f ?? "All");
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredEntries));
    }

    private void CopyToClipboard()
    {
        var lines = FilteredEntries.Select(e =>
        {
            var line = $"[{e.Timestamp:HH:mm:ss}] {e.Level,-7} {e.Message}";
            return e.Detail is { Length: > 0 } d ? line + "\n           " + d : line;
        });
        System.Windows.Forms.Clipboard.SetText(string.Join("\n", lines));
    }
}
