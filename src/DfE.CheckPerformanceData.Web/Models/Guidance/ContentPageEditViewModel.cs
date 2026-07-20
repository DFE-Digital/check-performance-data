using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Editor view model: the page's editable draft tree plus enough metadata to frame the editor and show
// publish state. Content is rendered by the recursive editor partials with add/move/delete controls.
// ActionBase is the URL prefix for widget-mutation form actions (WITHOUT trailing slash).
// Old content-page editor sets ActionBase = "/content-page/{slug}"; node editor sets
// ActionBase = "/admin/pages/{guid}/content". Slug is kept (nullable) for the old editor's
// public-preview link only — the node editor leaves it null.
public sealed class ContentPageEditViewModel
{
    public string? Slug { get; init; }
    public required string ActionBase { get; init; }
    public required string Title { get; init; }

    /// <summary>The node's URL segment (last path piece). Editable in the node-tree editor.</summary>
    public string Segment { get; init; } = string.Empty;

    /// <summary>Optional lede rendered above the page H1. Null when unset.</summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Optional override label used in the admin left-hand nav (and other tree/list surfaces).
    /// Falls back to <see cref="Title"/> when unset. Null when unset.
    /// </summary>
    public string? PageName { get; init; }

    /// <summary>Public menu visibility (from PageNode.ShowInMenu).</summary>
    public bool ShowInMenu { get; init; } = true;

    /// <summary>Search visibility (from PageNode.AppearInSearch).</summary>
    public bool AppearInSearch { get; init; } = true;

    /// <summary>Free-text search keywords (from PageNode.Keywords). Highest search weight.</summary>
    public string? Keywords { get; init; }

    public string Layout { get; init; } = string.Empty;
    public required IReadOnlyList<ContentNode> Content { get; init; }

    // True for the old slug-keyed content-page editor (shows the inline "Save and publish" form).
    // False for the node-tree editor, where publishing is done on the Versions page.
    public bool ShowInlinePublish { get; init; } = true;

    // Node-tree editor: carries the page node id so the view can POST to
    // /admin/pages/{NodeId}/publish-draft and /admin/pages/{NodeId}/unpublish.
    // Empty (Guid.Empty) when the old slug-keyed content-page editor is in use (retired).
    public Guid NodeId { get; init; }

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
