namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class CreateWikiPageViewModel
{
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int? ParentId { get; set; }

    // Optional custom URL slug. Empty derives the slug from the title; a value pins it.
    public string? Slug { get; set; }
}
