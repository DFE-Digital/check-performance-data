using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public sealed class CheckYourPupilDataService : ICheckYourPupilDataService
{
    private readonly ICheckYourPupilDataRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStudentResultsClient _studentResultsClient;
    private readonly ILogger<CheckYourPupilDataService> _log;

    public CheckYourPupilDataService(
        ICheckYourPupilDataRepository repository,
        ICurrentUserService currentUserService,
        IStudentResultsClient studentResultsClient,
        ILogger<CheckYourPupilDataService>? logger = null)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _studentResultsClient = studentResultsClient;
        _log = logger ?? NullLogger<CheckYourPupilDataService>.Instance;
    }

    public async Task<(PupilTable Table, int TotalCount)> GetPupilTableAsync(Guid windowId, bool included, string? search, int page, int pageSize)
    {
        var laestab = _currentUserService.OrganisationLaestab;
        var window = await _repository.GetCheckingWindowAsync(windowId);
        var (items, total) = await _repository.GetPupilPageAsync(windowId, laestab, included, search, page, pageSize);

        return (PupilTable.Build(PupilColumnSets.Table(window.CheckingWindowType, included), items), total);
    }

    public async Task<PupilTable> GetPupilCsvAsync(Guid windowId, bool included)
    {
        var laestab = _currentUserService.OrganisationLaestab;
        var window = await _repository.GetCheckingWindowAsync(windowId);
        var items = await _repository.GetAllPupilsAsync(windowId, laestab, included);

        return PupilTable.Build(PupilColumnSets.Csv(window.CheckingWindowType, included), items);
    }

    public Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => _repository.GetCheckingWindowAsync(windowId);

    public async Task<IReadOnlyList<PupilSuggestionDto>> GetPupilSuggestionsAsync(Guid windowId, string query, PupilFilter filter, Guid? excludeId = null, bool requireResults = false)
    {
        var laestab = _currentUserService.OrganisationLaestab;
        var urn = _currentUserService.OrganisationUrn;

        // A results enquiry names a student whose grade is wrong, so a student with no result is
        // not a candidate. The set comes from the same cached school file the enquiry itself reads,
        // and is resolved only when asked for — every other journey searches the whole roll.
        var withResults = requireResults
            ? await _studentResultsClient.GetStudentIdsWithResultsAsync(windowId, laestab)
            : null;

        return await _repository.SearchPupilsAsync(windowId, laestab, urn, query, filter, excludeId, withResults);
    }

    public async Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId)
    {
        var laestab = _currentUserService.OrganisationLaestab;
        return await _repository.GetPupilAsync(windowId, laestab, pupilId);
    }

    public async Task<PupilDuplicateCheckResult> DuplicateCheckAsync(Guid windowId, string firstname, string surname, string dateOfBirth)
    {
        var laestab = _currentUserService.OrganisationLaestab;

        try
        {
            var pupils = await _repository.GetAllPupilsForSchoolAsync(windowId, laestab);

            var displayDob = PupilDateFormatter.ToDisplayDate(dateOfBirth);
            var nameQuery = $"{firstname} {surname}";
            var matches = pupils
                .Where(p => PupilSuggestionFormat.NameMatchesSplitQuery(p.Firstname, p.Surname, nameQuery)
                         && PupilDateFormatter.ToDisplayDate(p.DateOfBirth) == displayDob)
                .Select(p => new DuplicateMatch
                {
                    Id = p.Id,
                    Firstname = p.Firstname,
                    Surname = p.Surname,
                    DateOfBirth = PupilDateFormatter.ToDisplayDate(p.DateOfBirth),
                    Identifier = p.Identifier,
                    IsIncluded = p.IsIncluded
                })
                .ToList();

            var result = PupilDuplicateCheckResult.Build(matches);

            // The match count and scenario are PII-free and safe to log; names/identifiers are not.
            _log.LogInformation(
                "Duplicate check returned {MatchCount} match(es) ({Scenario}) for window {WindowId}",
                result.Matches.Count, result.Scenario, windowId);

            return result;
        }
        catch (Exception ex)
        {
            // Spec edge case: a duplicate-check failure must not block the Add journey. Treat it
            // as no matches so the user continues, and log a PII-free error for investigation.
            _log.LogError(ex,
                "Duplicate check failed; treating as no matches for window {WindowId}", windowId);
            return PupilDuplicateCheckResult.None;
        }
    }

}
