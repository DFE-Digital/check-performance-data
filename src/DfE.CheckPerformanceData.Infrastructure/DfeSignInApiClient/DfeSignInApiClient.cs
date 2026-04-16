using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public sealed class DfeSignInApiClient(HttpClient httpClient) : IDfESignInApiClient
{
    public async Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId)
    {
        var userOrganisations = await httpClient.GetFromJsonAsync<List<OrganisationDto>>($"users/{userId}/organisations", 
            new JsonSerializerOptions()
            {
                Converters = { new OrganisationDtoJsonConverter() }
            });
        
        return userOrganisations?.FirstOrDefault(o => o.Id == organisationId);
    }
}

public class OrganisationDtoJsonConverter : JsonConverter<OrganisationDto>
{
    public override OrganisationDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        
        var dto = JsonSerializer.Deserialize<OrganisationDto>(root.GetRawText(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
        
        
        if (root.TryGetProperty("localAuthority", out var localAuthorityElement))
        {
            var orgCode = localAuthorityElement.GetProperty("code").GetString();
            var orgId = root.GetProperty("establishmentNumber").GetString();
        
            dto.LAESTAB = $"{orgCode}{orgId}";   
        }
        
        return dto;
    }

    public override void Write(Utf8JsonWriter writer, OrganisationDto value, JsonSerializerOptions options) => 
        throw new NotImplementedException();
}