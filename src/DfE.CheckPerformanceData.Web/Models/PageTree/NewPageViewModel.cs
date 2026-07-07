namespace DfE.CheckPerformanceData.Web.Models.PageTree;

// View model for the create-page form (/admin/pages/new).
// Carries submitted values back on re-display (validation failure) and an optional error message.
public sealed class NewPageViewModel
{
    public Guid? ParentId { get; init; }
    public string? ParentTitle { get; init; }
    public string PageType { get; init; } = "content";
    public string Segment { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Error { get; init; }
}
