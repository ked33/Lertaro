using Lertaro.Core.IndexV2.Search;
using System.Diagnostics;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
[DoNotParallelize]
public sealed class DirectoryScopeAllocationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SearchStreaming_ResolvedDirectoryScope_AvoidsPerResultPathAllocations()
    {
        using var fixture = BuildDrive(fileCount: 10_000);

        var unscopedBytes = MeasureAllocatedBytes(() => Search(fixture, "item", null));
        var scopedBytes = MeasureAllocatedBytes(() => Search(fixture, "item", @"C:\Scope"));
        var unscopedTime = MeasureMedianElapsed(() => Search(fixture, "item", null));
        var scopedTime = MeasureMedianElapsed(() => Search(fixture, "item", @"C:\Scope"));

        TestContext.WriteLine($"Unscoped: {unscopedBytes:N0} B; scoped: {scopedBytes:N0} B; delta: {scopedBytes - unscopedBytes:N0} B.");
        TestContext.WriteLine($"Median unscoped: {unscopedTime.TotalMilliseconds:N2} ms; scoped: {scopedTime.TotalMilliseconds:N2} ms.");

        Assert.IsLessThanOrEqualTo(unscopedBytes + 100_000L, scopedBytes,
            "A resolved directory scope must not rebuild one full path per matched row.");
        // The scoped search must preserve the same visible result count as the unscoped fixture.
        Assert.AreEqual(10, Search(fixture, "item", @"C:\Scope"));
    }

    [TestMethod]
    public void SearchStreaming_PathModeScope_AvoidsPerResultPathAllocations()
    {
        using var fixture = BuildDrive(fileCount: 10_000);

        var unscopedBytes = MeasureAllocatedBytes(() => Search(fixture, @"scope\ item", null));
        var scopedBytes = MeasureAllocatedBytes(() => Search(fixture, @"scope\ item", @"C:\Scope"));
        var unscopedTime = MeasureMedianElapsed(() => Search(fixture, @"scope\ item", null));
        var scopedTime = MeasureMedianElapsed(() => Search(fixture, @"scope\ item", @"C:\Scope"));

        TestContext.WriteLine($"Path mode unscoped: {unscopedBytes:N0} B; scoped: {scopedBytes:N0} B; delta: {scopedBytes - unscopedBytes:N0} B.");
        TestContext.WriteLine($"Path mode median unscoped: {unscopedTime.TotalMilliseconds:N2} ms; scoped: {scopedTime.TotalMilliseconds:N2} ms.");

        Assert.IsLessThanOrEqualTo(unscopedBytes + 100_000L, scopedBytes,
            "A resolved path-mode scope must not rebuild one full path per matched row.");
        Assert.AreEqual(10, Search(fixture, @"scope\ item", @"C:\Scope"));
    }

    private static long MeasureAllocatedBytes(Func<int> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = action(); // Warm this exact query's JIT, scope resolution, and pooled search buffers.
        // Matching can allocate on Parallel.For workers, so counting only the calling thread makes
        // identical searches differ with scheduling. This class already excludes other parallel tests.
        var before = GC.GetTotalAllocatedBytes(precise: true);
        _ = action();
        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    private static int Search(LiveIndexFixture fixture, string query, string? directoryFilter)
    {
        var count = 0;
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 10, _ => count++, CancellationToken.None, directoryFilter);
        return count;
    }

    private static TimeSpan MeasureMedianElapsed(Func<int> action)
    {
        var samples = new long[7];
        for (var i = 0; i < samples.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = action();
            samples[i] = Stopwatch.GetTimestamp() - start;
        }
        Array.Sort(samples);
        return TimeSpan.FromSeconds(samples[samples.Length / 2] / (double)Stopwatch.Frequency);
    }

    private static LiveIndexFixture BuildDrive(int fileCount)
    {
        var records = new List<FileRecord>(fileCount + 2)
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Scope", FileRecordFlags.Directory),
        };
        // Keep 10,000 matched rows but few unique names: this tests per-ROW scope allocations, not
        // parallel matcher chunk assignment or growth/trim of oversized pooled unique-name hit lists.
        // Repeated filenames live in distinct real directories, as they would in an actual index.
        for (var start = 0; start < fileCount; start += 100)
        {
            var parent = (UInt128)(records.Count + 1);
            records.Add(new FileRecord(parent, 2, $"Group{start / 100:D3}", FileRecordFlags.Directory));
            for (var i = 0; i < Math.Min(100, fileCount - start); i++)
                records.Add(new FileRecord((UInt128)(records.Count + 1), parent, $"item{i:D5}.txt", FileRecordFlags.None));
        }
        return LiveIndexFixture.Build("C", records);
    }
}
