using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface ICheckYourPupilDataService
{
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize);
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize);
    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
}

public class GetCheckYourPupilDataResult
{
    public required CheckingWindowDto Window { get; init; }
    public required List<PupilDto> IncludedPupils { get; init; }
    public required List<PupilDto> NonIncludedPupils { get; init; }
}

public class PupilDto
{
    public required string Firstname { get; init; }
    public required string Surname { get; init; }
    public required Guid Id { get; init; }
    public required string Sex { get; init; }
    public required string DateOfBirth { get; init; }
    public required string FirstLanguage { get; init; }
    public required int Age { get; init; }
}