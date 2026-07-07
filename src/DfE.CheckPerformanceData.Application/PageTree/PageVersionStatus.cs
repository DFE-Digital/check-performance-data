namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// Derives the display status of a page version for the version history list.
/// A superseded version (was live, replaced by a newer publish) shows "Past".
/// Only versions with no publish window at all are "Draft".
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
}
