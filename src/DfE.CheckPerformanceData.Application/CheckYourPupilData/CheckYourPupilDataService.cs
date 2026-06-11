using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public sealed class CheckYourPupilDataService(
    ICheckYourPupilDataRepository repository,
    ICurrentUserService currentUserService) : ICheckYourPupilDataService
{
    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize)
    {
        var laestab = currentUserService.OrganisationLaestab;
        return await repository.GetIncludedPupilsAsync(windowId, laestab, search, page, pageSize);
    }

    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize)
    {
        var laestab = currentUserService.OrganisationLaestab;
        return await repository.GetNonIncludedPupilsAsync(windowId, laestab, search, page, pageSize);
    }

    public Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => repository.GetCheckingWindowAsync(windowId);

    public async Task<IReadOnlyList<PupilCsvDto>> GetIncludedPupilsCsvAsync(Guid windowId)
    {
        var laestab = currentUserService.OrganisationLaestab;
        return await repository.GetAllIncludedPupilsAsync(windowId, laestab);
    }

    public async Task<IReadOnlyList<PupilCsvDto>> GetNonIncludedPupilsCsvAsync(Guid windowId)
    {
        var laestab = currentUserService.OrganisationLaestab;
        return await repository.GetAllNonIncludedPupilsAsync(windowId, laestab);
    }

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

