using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public sealed class CheckYourPupilDataService(
    ICheckYourPupilDataRepository repository,
    ICurrentUserService currentUserService,
    IStudentResultsClient studentResultsClient) : ICheckYourPupilDataService
{
    public async Task<(PupilTable Table, int TotalCount)> GetPupilTableAsync(Guid windowId, bool included, string? search, int page, int pageSize)
    {
        var laestab = currentUserService.OrganisationLaestab;
        var window = await repository.GetCheckingWindowAsync(windowId);
        var (items, total) = await repository.GetPupilPageAsync(windowId, laestab, included, search, page, pageSize);

        return (PupilTable.Build(PupilColumnSets.Table(window.CheckingWindowType, included), items), total);
    }

    public async Task<PupilTable> GetPupilCsvAsync(Guid windowId, bool included)
    {
        var laestab = currentUserService.OrganisationLaestab;
        var window = await repository.GetCheckingWindowAsync(windowId);
        var items = await repository.GetAllPupilsAsync(windowId, laestab, included);

        return PupilTable.Build(PupilColumnSets.Csv(window.CheckingWindowType, included), items);
    }

    public Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => repository.GetCheckingWindowAsync(windowId);

    public async Task<IReadOnlyList<PupilSuggestionDto>> GetPupilSuggestionsAsync(Guid windowId, string query, PupilFilter filter, Guid? excludeId = null, bool requireResults = false)
    {
        var laestab = currentUserService.OrganisationLaestab;
        var urn = currentUserService.OrganisationUrn;

        // A results enquiry names a student whose grade is wrong, so a student with no result is
        // not a candidate. The set comes from the same cached school file the enquiry itself reads,
        // and is resolved only when asked for — every other journey searches the whole roll.
        var withResults = requireResults
            ? await studentResultsClient.GetStudentIdsWithResultsAsync(windowId, laestab)
            : null;

        return await repository.SearchPupilsAsync(windowId, laestab, urn, query, filter, excludeId, withResults);
    }

    public async Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId)
    {
        var laestab = currentUserService.OrganisationLaestab;
        return await repository.GetPupilAsync(windowId, laestab, pupilId);
    }
}

