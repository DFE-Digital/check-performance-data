namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// Derives the display status of a page version for the version history list.
/// A version that was live and that a newer publish took over shows "Past".
/// Only versions with no publish window at all are "Draft". A version that another version
/// replaced (the embedded image move records this) shows "Replaced", whatever its window says.
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
    /// The status of a version. A replaced version is never live, scheduled or the working draft
    /// again, so it shows "Replaced".
    /// </summary>
    public static Status Of(PageNodeVersionDto version, DateTime nowUtc) =>
        version.IsSuperseded
            ? new("Replaced", "govuk-tag--grey")
            : Of(version.IsCurrent, version.PublishFrom, nowUtc);
}
