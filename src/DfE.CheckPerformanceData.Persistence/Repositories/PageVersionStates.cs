using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// Which page versions are current at a moment: the live version, a version scheduled to go live,
// and the working draft. Every other version is an older version, kept as history. The media
// library uses this to group where an image is used, and to move the pins of current versions to
// a replaced file. Both use this one rule, so they cannot disagree.
//
// Live is worked out from the publish windows, like LiveVersionResolver (latest start wins, then
// the higher version number), and not from the IsCurrent cache, which is stale once a scheduled
// version goes live. The working draft is the highest-numbered version with no publish window,
// as in PageNodeService.
internal static class PageVersionStates
{
    public static IQueryable<Guid> CurrentVersionIds(IPortalDbContext context, DateTime nowUtc)
    {
        var versions = context.PageNodeVersions;
        return versions
            .Where(v =>
                (v.PublishFrom != null && v.PublishFrom <= nowUtc && (v.PublishTo == null || v.PublishTo > nowUtc)
                    && !versions.Any(o => o.PageNodeId == v.PageNodeId
                        && o.PublishFrom != null && o.PublishFrom <= nowUtc && (o.PublishTo == null || o.PublishTo > nowUtc)
                        && (o.PublishFrom > v.PublishFrom || (o.PublishFrom == v.PublishFrom && o.VersionId > v.VersionId))))
                || v.PublishFrom > nowUtc
                || (v.PublishFrom == null
                    && !versions.Any(o => o.PageNodeId == v.PageNodeId && o.PublishFrom == null && o.VersionId > v.VersionId)))
            .Select(v => v.Id);
    }
}
