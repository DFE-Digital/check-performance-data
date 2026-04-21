namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class CheckYourPupilDataViewModel
{
    public required string WindowId { get; init; }
    public required IReadOnlyList<PupilRow> Pupils { get; init; }
    public required int PupilsPage { get; init; }
    public required int PupilsTotalPages { get; init; }
    public required IReadOnlyList<AssessmentResultRow> Results { get; init; }
    public required int ResultsPage { get; init; }
    public required int ResultsTotalPages { get; init; }
}

public class PupilRow
{
    public required string Firstname { get; init; }
    public required string Surname { get; init; }
    public required string DateOfBirth { get; init; }
    public required int Pincl { get; init; }
}

public class AssessmentResultRow
{
    public required string Name { get; init; }
    public required string Subject { get; init; }
    public required string Outcome { get; init; }
}

public class PaginationViewModel
{
    public required int CurrentPage { get; init; }
    public required int TotalPages { get; init; }
    public required string WindowId { get; init; }
    public required int OtherPage { get; init; }
    public required string PageParam { get; init; }
    public required string TabAnchor { get; init; }
}
