namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class EditWikiPageViewModel
{
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }

    // Optional custom URL slug. Empty leaves an auto slug following the title; a value pins it.
    public string? Slug { get; set; }
}
