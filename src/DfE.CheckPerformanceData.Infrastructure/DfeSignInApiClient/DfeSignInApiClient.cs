using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public sealed class DfeSignInApiClient(
    HttpClient httpClient,
    IOptions<DfeSigninSettings> settings,
    ILogger<DfeSignInApiClient> logger) : IDfESignInApiClient
{
    private const string OverrideLaestab = "DSI/TEST";
    public async Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId)
    {
        using var response = await httpClient.GetAsync($"users/{userId}/organisations");

        logger.LogInformation(
            "DfE Sign-in organisations lookup for user {UserId} returned HTTP {StatusCode}.",
            userId, (int)response.StatusCode);

        // DfE Sign-in returns 404 when the user has no organisations. Treat that
        // as "no organisation" rather than a hard error so sign-in does not crash.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(
                "DfE Sign-in returned HTTP 404 for organisations of user {UserId}; treating as no organisation.",
                userId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var userOrganisations = await response.Content.ReadFromJsonAsync<List<OrganisationDto>>(
            new JsonSerializerOptions()
            {
                Converters = { new OrganisationDtoJsonConverter() }
            });

        var match = userOrganisations?.FirstOrDefault(o => o.Id == organisationId);
        if (match == null)
            logger.LogWarning(
                "DfE Sign-in returned {Count} organisation(s) for user {UserId} but none matched organisation {OrgId} " +
                "(check for an id/casing mismatch between the sign-in token and the organisations list).",
                userOrganisations?.Count ?? 0, userId, organisationId);

        if (match.Laestab == OverrideLaestab)
        {
            logger.LogWarning("DfE Sign-in returned LAESTAB override for user {UserId}", userId);
        }
        return match;
    }

    public async Task<List<RoleDto>> GetUserRolesAsync(string orgId, string userid)
    {
        var serviceId = settings.Value.ServiceId;
        using var response = await httpClient.GetAsync(
            $"services/{serviceId}/organisations/{orgId}/users/{userid}");

        logger.LogInformation(
            "DfE Sign-in access lookup for user {UserId}, organisation {OrgId}, service {ServiceId} returned HTTP {StatusCode}.",
            userid, orgId, serviceId, (int)response.StatusCode);

        // DfE Sign-in returns 404 (not an empty list) when the user has no access
        // record for this service + organisation. That is a legitimate "no roles"
        // state, not a transport error, so treat it as an empty role set.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(
                "DfE Sign-in returned HTTP 404 for user {UserId} on organisation {OrgId} and service {ServiceId}; " +
                "treating as no roles. Verify DfeSignIn:ServiceId matches the production service registration and " +
                "that the user has a role assigned for it.",
                userid, orgId, serviceId);
            return [];
        }

        if (!response.IsSuccessStatusCode)
            logger.LogError(
                "DfE Sign-in access lookup for user {UserId}, organisation {OrgId}, service {ServiceId} failed with HTTP {StatusCode}.",
                userid, orgId, serviceId, (int)response.StatusCode);

        response.EnsureSuccessStatusCode();

        var userRoles = await response.Content.ReadFromJsonAsync<DfeUserAccessResponse>(
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (userRoles == null || userRoles.Roles.Count == 0)
        {
            logger.LogWarning(
                "DfE Sign-in returned HTTP 200 but no roles for user {UserId} on organisation {OrgId}, service {ServiceId}.",
                userid, orgId, serviceId);
            return [];
        }

        return userRoles.Roles;
    }



    public async Task<OrganisationUsersResponseDto?> GetOrganisationUsersAsync(string ukprn, string[]? roles = null)
    {
        var url = roles is { Length: > 0 }
            ? $"organisations/{ukprn}/users?roles={Uri.EscapeDataString(string.Join(",", roles))}"
            : $"organisations/{ukprn}/users";

        try
        {
            return await httpClient.GetFromJsonAsync<OrganisationUsersResponseDto>(
                url,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (JsonException)
        {
            return null;
        }
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
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        
        OrganisationDto? dto = JsonSerializer.Deserialize<OrganisationDto>(root.GetRawText(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

        if (dto?.Urn == "990082" && dto.Name == "DSI Test College")
        {
            dto.Laestab = "DSI/TEST";
            dto.StatutoryLowAge = 11;
            dto.StatutoryHighAge = 16;
            return dto;
        }

        if (!root.TryGetProperty("localAuthority", out var localAuthorityElement)) 
            return dto!;
        
        string? orgCode = localAuthorityElement.GetProperty("code").GetString();
        string? orgId = root.GetProperty("establishmentNumber").GetString();
        
        if (!string.IsNullOrEmpty(orgCode) && !string.IsNullOrEmpty(orgId))
        {
            dto?.Laestab = $"{orgCode}/{orgId}";    
        }
        
        return dto!;
    }

    public override void Write(Utf8JsonWriter writer, OrganisationDto value, JsonSerializerOptions options) => 
        throw new NotImplementedException();
}