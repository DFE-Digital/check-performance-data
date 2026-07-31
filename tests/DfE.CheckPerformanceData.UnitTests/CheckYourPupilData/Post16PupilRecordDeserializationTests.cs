using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// Guards the 16-19 supplier schemas (dave-16-19-Included-pupil-schema / dave-16-19-Nonincluded-pupil-schema).
// Deserializes through PupilDataBlobClient.JsonOptions so the tests bind exactly as production does.
public class Post16PupilRecordDeserializationTests
{
    private static Post16PupilRecord DeserializeSingle(string json)
    {
        var pupils = JsonSerializer.Deserialize<List<Post16PupilRecord>>(json, PupilDataBlobClient.JsonOptions);
        Assert.NotNull(pupils);
        return Assert.Single(pupils!);
    }

    [Fact]
    public void Maps_a_row_from_the_included_file()
    {
        const string json = """
        [
          {
            "Id": "11111111-1111-1111-1111-111111111111",
            "CheckingWindowId": "22222222-2222-2222-2222-222222222222",
            "INCLUDED": true,
            "CYPMD_ID": "500123",
            "SURNAME": "Smith",
            "FORENAMES": "Alice",
            "SEX": "F",
            "DOB": "2007-09-01 00:00:00.0000000",
            "AGE": 18,
            "P_INCL": 501,
            "P_INCL_aims": 503,
            "LAESTAB": "860/4070",
            "URN": "136309",
            "UKPRN": "10001234",
            "ULN": "9900112233",
            "TOTPTSE_ALEV": 123.45
          }
        ]
        """;

        var pupil = DeserializeSingle(json);

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), pupil.Id);
        Assert.Equal("500123", pupil.Cypmd_Id);
        Assert.Equal("Smith", pupil.Surname);
        Assert.Equal("Alice", pupil.Firstname);
        Assert.Equal("F", pupil.Sex);
        Assert.Equal("2007-09-01 00:00:00.0000000", pupil.DateOfBirth);
        Assert.Equal(18, pupil.Age);
        Assert.Equal(501, pupil.Pincl);
        Assert.Equal(503, pupil.PinclAims);
        Assert.Equal("860/4070", pupil.Laestab);
        Assert.Equal("136309", pupil.Urn);
        Assert.Equal("10001234", pupil.Ukprn);
        Assert.Equal("9900112233", pupil.Uln);
        Assert.Equal("9900112233", pupil.Identifier);
        Assert.True(pupil.IsIncluded);
    }

    [Fact]
    public void Maps_a_row_from_the_non_included_file_which_has_no_pincl_and_no_qualifications()
    {
        const string json = """
        [
          {
            "Id": "33333333-3333-3333-3333-333333333333",
            "INCLUDED": false,
            "CYPMD_ID": "500999",
            "SURNAME": "Jones",
            "FORENAMES": "Bob",
            "SEX": "M",
            "DOB": "2007-01-15 00:00:00.0000000",
            "AGE": 18,
            "LAESTAB": "860/4070",
            "URN": "136309",
            "UKPRN": "10001234",
            "ULN": "9900445566",
            "CampID_0": "C0",
            "CampID_1": "C1"
          }
        ]
        """;

        var pupil = DeserializeSingle(json);

        Assert.Null(pupil.Pincl);
        Assert.Null(pupil.PinclAims);
        Assert.False(pupil.IsIncluded);
        Assert.Equal("9900445566", pupil.Identifier);
        Assert.Equal("C0", pupil.CampId0);
        Assert.Equal("C1", pupil.CampId1);
    }

    [Fact]
    public void IsIncluded_follows_the_stamped_marker_not_the_pincl_code()
    {
        // 501 is a Post16 code and is NOT in the KS4 included list; inclusion must still be true
        // because the record came from the included file.
        var pupil = DeserializeSingle("""[ { "INCLUDED": true, "P_INCL": 501 } ]""");

        Assert.True(pupil.IsIncluded);
    }

    [Fact]
    public void Null_strings_become_empty_so_search_never_NREs()
    {
        var pupil = DeserializeSingle("""[ { "SURNAME": null, "ULN": null } ]""");

        Assert.Equal(string.Empty, pupil.Surname);
        Assert.Equal(string.Empty, pupil.Uln);
        Assert.Equal(string.Empty, pupil.Identifier);
    }

    [Fact]
    public void Binds_numeric_fields_sent_as_quoted_strings()
    {
        var pupil = DeserializeSingle("""[ { "AGE": "18", "P_INCL": "502" } ]""");

        Assert.Equal(18, pupil.Age);
        Assert.Equal(502, pupil.Pincl);
    }
}
