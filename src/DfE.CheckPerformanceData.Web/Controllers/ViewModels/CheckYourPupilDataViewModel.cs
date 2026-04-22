namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class CheckYourPupilDataViewModel
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
    public required string WindowTitle { get; init; }
}

public class PupilRow
{
    public required string Surname { get; init; }
    public required string Firstname { get; init; }
    public required string Sex { get; init; }
    public required string DateOfBirth { get; init; }
    public required int Age { get; init; }
    public required string FirstLanguage { get; init; }
}

public class PupilSearchViewModel
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
