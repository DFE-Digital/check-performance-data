using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// View model for a published content page. Content is the deserialised tree the partials render
// recursively; Nav is the auto-generated section list (headings) the side navigation renders.
public sealed class ContentPageViewModel
{
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? Caption { get; init; }
    public required string Layout { get; init; }
    public required IReadOnlyList<ContentNode> Content { get; init; }
    public required IReadOnlyList<ContentNavItem> Nav { get; init; }
}
