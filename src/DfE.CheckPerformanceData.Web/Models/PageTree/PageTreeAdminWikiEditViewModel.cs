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
}
