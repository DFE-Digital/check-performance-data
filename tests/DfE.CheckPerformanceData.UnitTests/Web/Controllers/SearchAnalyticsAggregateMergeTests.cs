using System.Reflection;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Unit-level coverage of SearchAnalyticsController.MergeAggregateVolumeAndUniqueSeries.
// The merge is invoked in the aggregate-mode branch of the Index action to combine two
// aggregate readers (volume + unique-sessions) into one dual-axis series. Both readers
// return the same 168-cell (weekday, hour) spine in production, but if the two ever
// return divergent counts (schema drift, new bucket dimension) the merged output must
// be safe — this test pins the three shape cases so a future refactor cannot silently
// change behaviour.
//
// Method is private static so we invoke via reflection. Straightforward: two
// IReadOnlyList<VolumeBucket> in, one IReadOnlyList<VolumeBucket> out.
public sealed class SearchAnalyticsAggregateMergeTests
{
    private static readonly MethodInfo MergeMethod = typeof(SearchAnalyticsController)
        .GetMethod(
            "MergeAggregateVolumeAndUniqueSeries",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "Expected private static SearchAnalyticsController.MergeAggregateVolumeAndUniqueSeries to exist.");

    private static IReadOnlyList<VolumeBucket> Merge(
        IReadOnlyList<VolumeBucket> volume, IReadOnlyList<VolumeBucket> unique)
    {
        var result = MergeMethod.Invoke(null, [volume, unique]);
        return Assert.IsAssignableFrom<IReadOnlyList<VolumeBucket>>(result);
    }

    private static VolumeBucket[] Spine(int size, int searchCount, int uniqueCount)
    {
        var anchor = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cells = new VolumeBucket[size];
        for (var i = 0; i < size; i++)
        {
            cells[i] = new VolumeBucket(anchor.AddHours(i), searchCount, uniqueCount);
        }
        return cells;
    }

    // Case 1: both readers return the 168-cell weekday-hour spine. The merged output has
    // 168 buckets where each carries its volume-reader SearchCount and the unique-reader
    // SearchCount surfaces as UniqueSessionCount. This is the happy path in prod — the
    // dual-axis chart reads both fields off the merged bucket.
    [Fact]
    public void Merge_BothReadersFull168_ReturnsMerged168BucketsWithBothCounts()
    {
        var volumeCells = Spine(168, searchCount: 12, uniqueCount: 0);
        var uniqueCells = Spine(168, searchCount: 4, uniqueCount: 0);

        var merged = Merge(volumeCells, uniqueCells);

        Assert.Equal(168, merged.Count);
        for (var i = 0; i < 168; i++)
        {
            Assert.Equal(volumeCells[i].BucketStart, merged[i].BucketStart);
            Assert.Equal(12, merged[i].SearchCount);
            Assert.Equal(4, merged[i].UniqueSessionCount);
        }
    }

    // Case 2: volume reader returns 168 cells; unique-sessions reader returns fewer
    // (schema drift, partial-window failure). The current implementation defends by
    // falling back to the volume series unchanged (unique-session count stays 0). This
    // is a deliberate design choice — commented "so the chart still renders rather than
    // throwing on a shape mismatch". Test pins that behaviour so a future refactor is
    // forced to acknowledge the choice.
    [Fact]
    public void Merge_UniqueReaderShorterThanVolume_ReturnsVolumeUnchanged()
    {
        var volumeCells = Spine(168, searchCount: 7, uniqueCount: 0);
        var uniqueCells = Spine(150, searchCount: 3, uniqueCount: 0);

        var merged = Merge(volumeCells, uniqueCells);

        Assert.Equal(168, merged.Count);
        // Each bucket comes through with the volume SearchCount and UniqueSessionCount=0.
        Assert.All(merged, bucket =>
        {
            Assert.Equal(7, bucket.SearchCount);
            Assert.Equal(0, bucket.UniqueSessionCount);
        });
    }

    // Case 3: both readers return empty. Merged output is also empty — the length-
    // equality guard hits with 0 == 0 so the positional loop is a no-op.
    [Fact]
    public void Merge_BothReadersEmpty_ReturnsEmpty()
    {
        var merged = Merge(Array.Empty<VolumeBucket>(), Array.Empty<VolumeBucket>());
        Assert.Empty(merged);
    }
}
