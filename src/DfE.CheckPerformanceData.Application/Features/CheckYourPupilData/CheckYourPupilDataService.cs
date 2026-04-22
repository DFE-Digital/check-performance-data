using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;

public sealed class CheckYourPupilDataService(
    ICheckYourPupilDataRepository repository,
    IDfESignInApiClient apiClient,
    ICurrentUserService currentUserService) : ICheckYourPupilDataService
{
    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize)
    {
        var laestab = await GetLaestabAsync();
        return await repository.GetIncludedPupilsAsync(windowId, laestab, search, page, pageSize);
    }

    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize)
    {
        var laestab = await GetLaestabAsync();
        return await repository.GetNonIncludedPupilsAsync(windowId, laestab, search, page, pageSize);
    }

    public Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => repository.GetCheckingWindowAsync(windowId);

    private async Task<string> GetLaestabAsync()
    {
        var organisation = await apiClient.GetOrganisationAsync(
            currentUserService.UserId,
            currentUserService.OrganisationId);

        return organisation?.Laestab
            ?? throw new InvalidOperationException($"Organisation not found for user {currentUserService.UserId}.");
    }
}
