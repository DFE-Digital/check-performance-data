using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Models.PageTree;

public sealed class PageTreeAdminWikiEditViewModel
{
    public Guid NodeId { get; init; }
    public required string NodeTitle { get; init; }
    public string Content { get; init; } = string.Empty;

    /// <summary>The node's slug path (no leading slash), used for the "View page" link.</summary>
    public string PagePath { get; init; } = string.Empty;

    /// <summary>True when the page node currently has a live (IsCurrent) version.</summary>
    public bool IsPublished { get; init; }

    /// <summary>All versions, newest first (as GetVersionsAsync returns them).</summary>
    public IReadOnlyList<PageNodeVersionDto> Versions { get; init; } = [];

    /// <summary>Display label of the currently-live version (e.g. "1"), or null if none is live.</summary>
    public string? PublishedVersionLabel =>
        Versions.FirstOrDefault(v => v.IsCurrent) is { } live
            ? PageVersionNumbering.Label(Versions, live)
            : null;

    /// <summary>
    /// Display label of the working draft (newest version with MinorVersion >= 1); if none and the
    /// page is not live, the latest version is surfaced as the draft. Null only when there are no versions.
    /// </summary>
    public string? DraftVersionLabel
    {
        get
        {
            var draft = Versions.FirstOrDefault(v => v.MinorVersion >= 1);
            if (draft is null && PublishedVersionLabel is null) draft = Versions.FirstOrDefault();
            return draft is null ? null : PageVersionNumbering.Label(Versions, draft);
        }
    }
}
