using DfE.CheckPerformanceData.Application.ContentPages;

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
    public string Layout { get; init; } = string.Empty;
    public required IReadOnlyList<ContentNode> Content { get; init; }
    public int? PublishedVersionNumber { get; init; }

    // True for the old slug-keyed content-page editor (shows the inline "Save and publish" form).
    // False for the node-tree editor, where publishing is done on the Versions page.
    public bool ShowInlinePublish { get; init; } = true;
}
