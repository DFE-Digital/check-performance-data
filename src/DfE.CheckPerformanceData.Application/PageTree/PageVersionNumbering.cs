namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// Derives human-readable version labels from a list of versions. Whole-integer scheme —
/// every version is displayed as a single integer, whether published or still a working draft.
/// A draft is labelled with the integer it will become when published (the next major after the
/// most recent live version), and that label does not change as the draft is edited.
/// </summary>
public static class PageVersionNumbering
{
    /// <summary>
    /// Returns the major number for a version within the given ordered list. Published versions
    /// count as themselves (1st publish → 1, 2nd publish → 2, …). A draft takes the next major
    /// after however many publishes existed at the time it was created — so a draft opened
    /// against v2 is labelled "3".
    /// </summary>
    public static int MajorFor(IReadOnlyList<PageNodeVersionDto> all, PageNodeVersionDto v)
    {
        var publishedUpToInclusive = all.Count(x => x.MinorVersion == 0 && x.VersionId <= v.VersionId);
        return v.MinorVersion == 0
            ? publishedUpToInclusive
            : publishedUpToInclusive + 1;
    }

    /// <summary>
    /// Returns the display label — always the major number as a plain integer.
    /// </summary>
    public static string Label(IReadOnlyList<PageNodeVersionDto> all, PageNodeVersionDto v) =>
        MajorFor(all, v).ToString();
}
