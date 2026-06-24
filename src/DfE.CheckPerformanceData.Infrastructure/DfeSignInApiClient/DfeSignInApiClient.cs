using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public sealed class DfeSignInApiClient(HttpClient httpClient, IOptions<DfeSigninSettings> settings) : IDfESignInApiClient
{
    public async Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId)
    {
        using var response = await httpClient.GetAsync($"users/{userId}/organisations");

        // DfE Sign-in returns 404 when the user has no organisations. Treat that
        // as "no organisation" rather than a hard error so sign-in does not crash.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var userOrganisations = await response.Content.ReadFromJsonAsync<List<OrganisationDto>>(
            new JsonSerializerOptions()
            {
                Converters = { new OrganisationDtoJsonConverter() }
            });

        return userOrganisations?.FirstOrDefault(o => o.Id == organisationId);
    }

    public async Task<List<RoleDto>> GetUserRolesAsync(string orgId, string userid)
    {
        var serviceId = settings.Value.ServiceId;
        using var response = await httpClient.GetAsync(
            $"services/{serviceId}/organisations/{orgId}/users/{userid}");

        // DfE Sign-in returns 404 (not an empty list) when the user has no access
        // record for this service + organisation. That is a legitimate "no roles"
        // state, not a transport error, so treat it as an empty role set.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();

        var userRoles = await response.Content.ReadFromJsonAsync<DfeUserAccessResponse>(
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (userRoles == null || userRoles.Roles.Count == 0)
            return [];

        return userRoles.Roles;
    }
    
    private class DfeUserAccessResponse
    {
        public List<RoleDto> Roles { get; init; } = [];
    }
}



public sealed class OrganisationDtoJsonConverter : JsonConverter<OrganisationDto>
{
    public override OrganisationDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        
        var dto = JsonSerializer.Deserialize<OrganisationDto>(root.GetRawText(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

        if (!root.TryGetProperty("localAuthority", out var localAuthorityElement)) return dto!;
        
        
        var orgCode = localAuthorityElement.GetProperty("code").GetString();
        var orgId = root.GetProperty("establishmentNumber").GetString();

        dto?.Laestab = $"{orgCode}/{orgId}";

        return dto!;
    }

    public override void Write(Utf8JsonWriter writer, OrganisationDto value, JsonSerializerOptions options) => 
        throw new NotImplementedException();
}