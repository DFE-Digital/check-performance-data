namespace DfE.CheckPerformanceData.Web.Models.PageTree;

public sealed class PageNodeDeleteViewModel
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public bool HasChildren { get; init; }
    public string? Error { get; init; }
}
