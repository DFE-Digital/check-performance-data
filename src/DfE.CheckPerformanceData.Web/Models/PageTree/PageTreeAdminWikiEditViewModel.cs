namespace DfE.CheckPerformanceData.Web.Models.PageTree;

public sealed class PageTreeAdminWikiEditViewModel
{
    public Guid NodeId { get; init; }
    public required string NodeTitle { get; init; }
    public string Content { get; init; } = string.Empty;
}
