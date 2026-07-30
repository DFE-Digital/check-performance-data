using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public sealed class CheckYourPupilDataService(
    ICheckYourPupilDataRepository repository,
    ICurrentUserService currentUserService) : ICheckYourPupilDataService
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

    public async Task<IReadOnlyList<PupilSuggestionDto>> GetPupilSuggestionsAsync(Guid windowId, string query, PupilFilter filter, Guid? excludeId = null)
    {
        var laestab = currentUserService.OrganisationLaestab;
        var urn = currentUserService.OrganisationUrn;
        return await repository.SearchPupilsAsync(windowId, laestab, urn, query, filter, excludeId);
    }

    public async Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId)
    {
        var laestab = currentUserService.OrganisationLaestab;
        return await repository.GetPupilAsync(windowId, laestab, pupilId);
    }
}

