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

    /// <summary>VersionId of the currently-live version, or null if none is live.</summary>
    public int? PublishedVersionId => Versions.FirstOrDefault(v => v.IsCurrent)?.VersionId;

    /// <summary>
    /// VersionId of the working draft (newest version with no publish window); if none and the
    /// page is not live, the latest version is surfaced as the draft. Null only when there are no versions.
    /// </summary>
    public int? DraftVersionId
    {
        get
        {
            var working = Versions.FirstOrDefault(v => v.PublishFrom is null);
            if (working is not null) return working.VersionId;
            return PublishedVersionId is null ? Versions.FirstOrDefault()?.VersionId : null;
        }
    }
}
