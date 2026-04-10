using System.Net.Http.Json;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public sealed class DfeSignInApiClient(HttpClient httpClient) : IDfESignInApiClient
{
    public async Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId)
    {
        var userOrganisations = await httpClient.GetFromJsonAsync<List<OrganisationDto>>($"users/{userId}/organisations");
        
        return userOrganisations?.FirstOrDefault(o => o.Id == organisationId);
    }
}