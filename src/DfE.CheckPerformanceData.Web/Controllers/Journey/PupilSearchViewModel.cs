using DfE.CheckPerformanceData.Application.Journey;
using Microsoft.AspNetCore.Mvc.Razor;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class PupilSearchViewModel
{
    public Guid WindowId { get; set; }
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public PupilFilter Filter { get; set; }
    public Guid? ExcludePupilId { get; set; }
    public string? SelectedPupilId { get; set; }
    public string? SelectedPupilLabel { get; set; }
    public string? Hint { get; set; }
}
