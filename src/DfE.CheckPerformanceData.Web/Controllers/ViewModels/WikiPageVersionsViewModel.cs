using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class WikiPageVersionsViewModel
{
    public WikiPageDto Page { get; set; } = null!;
    public List<WikiPageVersionDto> Versions { get; set; } = [];
}
