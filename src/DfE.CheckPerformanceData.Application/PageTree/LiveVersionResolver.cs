namespace DfE.CheckPerformanceData.Application.PageTree;

// Which version is live at a moment: the one whose publish window contains nowUtc, latest-start
// wins. A version with no PublishFrom is a draft and never live.
public static class LiveVersionResolver
{
    public static int? Resolve(IEnumerable<PageVersionWindow> versions, DateTime nowUtc) =>
        versions
            .Where(v => v.PublishFrom is { } from && from <= nowUtc
                        && (v.PublishTo is null || v.PublishTo > nowUtc))
            .OrderByDescending(v => v.PublishFrom)
            .Select(v => (int?)v.VersionId)
            .FirstOrDefault();
}
