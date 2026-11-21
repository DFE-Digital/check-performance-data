namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// Derives the display status of a page version for the version history list.
/// A superseded version (was live, replaced by a newer publish) shows "Past".
/// Only versions with no publish window at all are "Draft". A scheduled version that a newer
/// version on the same window replaces shows "Replaced".
/// </summary>
public static class PageVersionStatus
{
    public readonly record struct Status(string Label, string TagClass);

    public static Status Of(bool isCurrent, DateTime? publishFrom, DateTime nowUtc)
    {
        if (isCurrent) return new("Live", "govuk-tag--green");
        if (publishFrom is { } f && f > nowUtc) return new("Scheduled", "govuk-tag--blue");
        if (publishFrom is not null) return new("Past", "govuk-tag--grey");
        return new("Draft", "govuk-tag--grey");
    }

    /// <summary>
    /// The status of a version among all the page's versions. A scheduled version is "Replaced"
    /// when a higher-numbered version has the same start and lasts at least as long: that version
    /// goes live instead, so this one never will. Moving embedded images replaces one this way.
    /// </summary>
    public static Status Of(PageNodeVersionDto version, IReadOnlyCollection<PageNodeVersionDto> all, DateTime nowUtc)
    {
        if (!version.IsCurrent && version.PublishFrom is { } from && from > nowUtc
            && all.Any(o => o.VersionId > version.VersionId && o.PublishFrom == from
                && (o.PublishTo is null || (version.PublishTo is { } to && o.PublishTo >= to))))
            return new("Replaced", "govuk-tag--grey");
        return Of(version.IsCurrent, version.PublishFrom, nowUtc);
    }
}
