using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// PupilDto is persisted in ISession and in the requests/{ref}.json blob. The C# property was
// generalised Upn -> Identifier for Post16 (which has ULN, not UPN); the serialised name must
// stay "Upn" so drafts written before the rename still deserialise.
public class PupilDtoWireNameTests
{
    private static PupilDto Sample() => new()
    {
        Id = Guid.NewGuid(),
        Firstname = "Alice",
        Surname = "Smith",
        Sex = "F",
        DateOfBirth = "01/09/2010",
        Age = 15,
        Cypmd_Id = "000123",
        Identifier = "A860407000001B"
    };

    [Fact]
    public void Serialises_identifier_as_Upn()
    {
        string json = JsonSerializer.Serialize(Sample());

        Assert.Contains("\"Upn\":\"A860407000001B\"", json);
        Assert.DoesNotContain("Identifier", json);
    }

    [Fact]
    public void Deserialises_a_pre_rename_draft()
    {
        const string json = """
        {"Id":"11111111-1111-1111-1111-111111111111","Firstname":"Alice","Surname":"Smith",
         "Sex":"F","DateOfBirth":"01/09/2010","Age":15,"Cypmd_Id":"000123","Upn":"A860407000001B","Pincl":401}
        """;

        var dto = JsonSerializer.Deserialize<PupilDto>(json);

        Assert.NotNull(dto);
        Assert.Equal("A860407000001B", dto!.Identifier);
    }
}
