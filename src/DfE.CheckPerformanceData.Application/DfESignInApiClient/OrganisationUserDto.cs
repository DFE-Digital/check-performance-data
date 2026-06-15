using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public sealed class OrganisationUserDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public int UserStatus { get; init; }
    public List<UserRoleDto> Roles { get; init; } = [];
}

[JsonConverter(typeof(UserRoleDtoConverter))]
public sealed class UserRoleDto
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

internal sealed class UserRoleDtoConverter : JsonConverter<UserRoleDto>
{
    public override UserRoleDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var code = reader.GetString();
            return new UserRoleDto { Code = code ?? string.Empty };
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new UserRoleDto
        {
            Code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() ?? string.Empty : string.Empty,
            Name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty
        };
    }

    public override void Write(Utf8JsonWriter writer, UserRoleDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("code", value.Code);
        writer.WriteString("name", value.Name);
        writer.WriteEndObject();
    }
}

public sealed class OrganisationUsersResponseDto
{
    public List<OrganisationUserDto> Users { get; init; } = [];
    public int NumberOfRecords { get; init; }
    public int Page { get; init; }
    public int NumberOfPages { get; init; }
    
}
