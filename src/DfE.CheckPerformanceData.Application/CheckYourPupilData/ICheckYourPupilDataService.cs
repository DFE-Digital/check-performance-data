using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface ICheckYourPupilDataService
{
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize);
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize);
    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
    Task<IReadOnlyList<PupilCsvDto>> GetIncludedPupilsCsvAsync(Guid windowId);
    Task<IReadOnlyList<PupilCsvDto>> GetNonIncludedPupilsCsvAsync(Guid windowId);
    Task<IReadOnlyList<PupilSuggestionDto>> GetPupilSuggestionsAsync(Guid windowId, string query,
        WhatToChange? whatToChange);

    Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId);
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

public record PupilSuggestionDto(Guid Id, string Label);

public class PupilCsvDto
{
    public required string Upn { get; init; }
    public required string CypmdId { get; init; }
    public required string Surname { get; init; }
    public required string Firstname { get; init; }
    public required string Sex { get; init; }
    public required string DateOfBirth { get; init; }
    public required int Age { get; init; }
    public required int Pincl { get; init; }
    public required string Laestab { get; init; }
    public required string Urn { get; init; }
    public required DateTime EntryDate { get; init; }
    public required string SenF { get; init; }
    public required string FirstLanguage { get; init; }
    public required string Ethnicity { get; init; }
    public required string ActualYearGroup { get; init; }
    public required bool NewMobile { get; init; }
}