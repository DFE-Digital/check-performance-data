using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Editor view model: the page's editable draft tree plus enough metadata to frame the editor and show
// publish state. Content is rendered by the recursive editor partials with add/move/delete controls.
public sealed class ContentPageEditViewModel
{
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string Layout { get; init; }
    public required IReadOnlyList<ContentNode> Content { get; init; }
    public int? PublishedVersionNumber { get; init; }
}
