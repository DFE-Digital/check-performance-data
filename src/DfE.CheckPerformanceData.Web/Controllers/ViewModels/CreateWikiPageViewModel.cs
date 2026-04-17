namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class CreateWikiPageViewModel
{
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int? ParentId { get; set; }
}
