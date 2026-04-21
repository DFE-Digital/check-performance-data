using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class HelpViewModel
{
    public List<WikiPageTreeNodeDto> NavigationTree { get; set; } = [];
    public WikiPageDto? CurrentPage { get; set; }
    public string CurrentSlugPath { get; set; } = string.Empty;
    public bool IsEditMode { get; set; }
}
