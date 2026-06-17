using System.Runtime;

namespace BaudioApp.Helpers;

/// Honest memory trimming: a full GC with one-off Large Object Heap compaction
/// actually returns managed heap segments to the OS. Throttled so it can't fire
/// repeatedly. Note: this does NOT use EmptyWorkingSet — that only pages memory
/// out to make Task Manager's number look small without freeing anything real.
internal static class MemoryHelper
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);
    private static DateTime _last = DateTime.MinValue;

    public static void TrimNow()
    {
        var now = DateTime.UtcNow;
        if (now - _last < MinInterval) return;
        _last = now;

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }
}
