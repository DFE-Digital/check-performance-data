namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public class PaginationViewModel
{
    public required int CurrentPage { get; init; }
    public required int TotalPages { get; init; }
    public required string WindowId { get; init; }
    public required int OtherPage { get; init; }
    public required string PageParam { get; init; }
    public required string TabAnchor { get; init; }
    public string? IncludedSearch { get; init; }
    public string? NonIncludedSearch { get; init; }
}