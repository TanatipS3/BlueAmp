using BaudioApp.Models;
using BaudioApp.Services;
using BaudioApp.ViewModels;
using Xunit;

namespace BaudioApp.Tests;

public class DiagnosticTests
{
    [Fact]
    public void Only_errors_are_kept_when_verbose_logging_is_off()
    {
        var diag = new DiagnosticService { VerboseEnabled = false };

        diag.Info("routine");
        diag.Warn("careful");
        diag.Error("broken");

        var entry = Assert.Single(diag.Entries);
        Assert.Equal(DiagnosticLevel.Error, entry.Level);
    }

    [Fact]
    public void Log_never_grows_past_500_entries_and_drops_the_oldest()
    {
        var diag = new DiagnosticService { VerboseEnabled = true };

        for (int i = 0; i < 520; i++) diag.Info($"entry {i}");

        Assert.Equal(500, diag.Entries.Count);
        Assert.Equal("entry 20", diag.Entries[0].Message);
        Assert.Equal("entry 519", diag.Entries[^1].Message);
    }

    [Fact]
    public void Each_filter_selects_the_entries_it_is_named_for()
    {
        var diag = new DiagnosticService { VerboseEnabled = true };
        diag.Info("connecting", tags: ["A2DP"]);
        diag.Info("device found", tags: ["System"]);
        diag.Error("connect failed", null, "A2DP", "Error");
        var vm = new DiagnosticViewModel(diag);

        vm.ActiveFilter = "Connection";
        Assert.Equal(["connecting", "connect failed"], vm.FilteredEntries.Select(e => e.Message));

        vm.ActiveFilter = "System";
        Assert.Equal(["device found"], vm.FilteredEntries.Select(e => e.Message));

        vm.ActiveFilter = "Errors";
        Assert.Equal(["connect failed"], vm.FilteredEntries.Select(e => e.Message));

        vm.ActiveFilter = "All";
        Assert.Equal(3, vm.FilteredEntries.Count());
    }

    [Fact]
    public void Every_toolbar_filter_matches_at_least_one_tag_the_app_really_logs()
    {
        // Guards against a filter like the old "Audio" one, which no entry ever matched.
        var diag = new DiagnosticService { VerboseEnabled = true };
        diag.Info("a", tags: ["A2DP"]);
        diag.Info("s", tags: ["System"]);
        diag.Error("e", null, "A2DP");
        var vm = new DiagnosticViewModel(diag);

        foreach (var filter in DiagnosticViewModel.Filters)
        {
            vm.ActiveFilter = filter;
            Assert.NotEmpty(vm.FilteredEntries);
        }
    }
}
