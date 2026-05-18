using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed class CheckYourPupilDataViewModel
{
    public required string WindowId { get; init; }
    public required IReadOnlyList<PupilRow> IncludedPupils { get; init; }
    public required int IncludedPupilsPage { get; init; }
    public required int IncludedPupilsTotalPages { get; init; }
    public string? IncludedSearch { get; init; }
    public required IReadOnlyList<PupilRow> NonIncludedPupils { get; init; }
    public required int NonIncludedPupilsPage { get; init; }
    public required int NonIncludedPupilsTotalPages { get; init; }
    public string? NonIncludedSearch { get; init; }
    public required string WindowEndDate { get; init; }
    public required string WindowEndTime { get; init; }
    public required string WindowTitle { get; init; }
    public NextSteps? SelectedNextStep { get; init; }
    public required bool IsWindowOpen { get; init; }
    public required string OrganisationName { get; init; }
}
