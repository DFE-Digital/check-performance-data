using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public class CheckYourPupilDataService(
    ICheckYourPupilDataRepository repository,
    IDfESignInApiClient apiClient,
    ICurrentUserService currentUserService) : ICheckYourPupilDataService
{
    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize)
    {
        var urn = await GetUrnAsync();
        return await repository.GetIncludedPupilsAsync(windowId, urn, search, page, pageSize);
    }

    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize)
    {
        var urn = await GetUrnAsync();
        return await repository.GetNonIncludedPupilsAsync(windowId, urn, search, page, pageSize);
    }

    public Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => repository.GetCheckingWindowAsync(windowId);

    public async Task<IReadOnlyList<PupilCsvDto>> GetIncludedPupilsCsvAsync(Guid windowId)
    {
        var urn = await GetUrnAsync();
        return await repository.GetAllIncludedPupilsAsync(windowId, urn);
    }

    public async Task<IReadOnlyList<PupilCsvDto>> GetNonIncludedPupilsCsvAsync(Guid windowId)
    {
        var urn = await GetUrnAsync();
        return await repository.GetAllNonIncludedPupilsAsync(windowId, urn);
    }

    public async Task<IReadOnlyList<PupilSuggestionDto>> GetPupilSuggestionsAsync(Guid windowId, string query, WhatToChange? whatToChange)
    {
        var urn = await GetUrnAsync();
        var included = whatToChange != WhatToChange.Include;
        return await repository.SearchPupilsAsync(windowId, urn, query, included);
    }

    public async Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId)
        => await repository.GetPupilAsync(windowId, pupilId);

    private async Task<string> GetUrnAsync()
    {
        var organisation = await apiClient.GetOrganisationAsync(
            currentUserService.UserId,
            currentUserService.OrganisationId);

        return organisation?.Urn
               ?? throw new InvalidOperationException($"Organisation not found for user {currentUserService.UserId}.");
    }
}

