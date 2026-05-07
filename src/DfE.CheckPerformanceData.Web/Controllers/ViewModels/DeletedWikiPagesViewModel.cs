using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class DeletedWikiPagesViewModel
{
    public List<DeletedWikiPageDto> DeletedPages { get; set; } = [];
    public List<WikiParentOptionDto> AvailableParents { get; set; } = [];
}
