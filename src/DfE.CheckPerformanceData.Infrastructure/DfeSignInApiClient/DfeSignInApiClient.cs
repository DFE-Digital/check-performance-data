using System.Net.Http.Json;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public class DfeSignInApiClient : IDfESignInApiClient
{
    private readonly HttpClient _httpClient;

    public DfeSignInApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    
    public async Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId)
    {
        var userOrganisations = await _httpClient.GetFromJsonAsync<List<OrganisationDto>>($"users/{userId}/organisations");
        
        return userOrganisations?.FirstOrDefault(o => o.Id == organisationId);
    }
}