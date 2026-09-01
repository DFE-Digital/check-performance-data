namespace DfE.CheckPerformanceData.Application.PageTree;

// Which version is live at a moment: the one whose publish window contains nowUtc, latest-start
// wins. A version with no PublishFrom is a draft and never live.
public static class LiveVersionResolver
{
    // Ties on PublishFrom are broken by the later VersionId. Two versions published in the same
    // tick is unusual but reachable — a scripted publish, or a bundle import replaying windows —
    // and without a tie-break the winner depends on the order rows happen to come back in, so
    // the same data could render differently on two pods, or before and after an export.
    public static int? Resolve(IEnumerable<PageVersionWindow> versions, DateTime nowUtc) =>
        versions
            .Where(v => v.PublishFrom is { } from && from <= nowUtc
                        && (v.PublishTo is null || v.PublishTo > nowUtc))
            .OrderByDescending(v => v.PublishFrom)
            .ThenByDescending(v => v.VersionId)
            .Select(v => (int?)v.VersionId)
            .FirstOrDefault();
}
