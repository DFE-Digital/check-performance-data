using System.Text.Json;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.UnitTests.DfESignInApiClient;

public class OrganisationDtoTests
{
    [Fact]
    public void Deserialize_PopulatesTypeId_FromTypeIdField()
    {
        const string json = """
        {
          "id": "5760D65B-1AAD-4E89-98DB-6A0ACC424042",
          "name": "A School",
          "urn": "142313",
          "type": { "id": "11", "name": "Other Independent School" }
        }
        """;

        var dto = JsonSerializer.Deserialize<OrganisationDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(dto);
        Assert.Equal("11", dto!.Type?.Id);
    }

    [Fact]
    public void Deserialize_TypeIsNull_WhenAbsent()
    {
        const string json = """
        {
          "id": "5760D65B-1AAD-4E89-98DB-6A0ACC424042",
          "name": "A School",
          "urn": "142313"
        }
        """;

        var dto = JsonSerializer.Deserialize<OrganisationDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(dto);
        Assert.Null(dto!.Type);
    }
}
