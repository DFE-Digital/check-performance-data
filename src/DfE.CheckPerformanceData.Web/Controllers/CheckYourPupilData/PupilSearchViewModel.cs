namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed class PupilSearchViewModel
{
    public required string WindowId { get; init; }
    public required string InputId { get; init; }
    public required string InputName { get; init; }
    public string? CurrentValue { get; init; }
    public required string ActiveTab { get; init; }
    public required string OtherPageName { get; init; }
    public required int OtherPage { get; init; }
    public required string OtherSearchName { get; init; }
    public string? OtherSearch { get; init; }
}