using DfE.CheckPerformanceData.Application.Journey;

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
    public string? BackPageId { get; set; }
    public bool BackPageIsPupilSearch { get; set; }
    public string? ConflictErrorReference { get; set; }
    public string? ConflictErrorLink { get; set; }
    public string? ConflictPupilName { get; set; }
    public string? ConflictReasonType { get; set; }
    public string? ConflictUserName { get; set; }
    public string? ConflictAttentionHtml { get; set; }
}
