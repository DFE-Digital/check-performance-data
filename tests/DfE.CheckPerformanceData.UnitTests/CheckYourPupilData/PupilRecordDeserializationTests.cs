using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// Guards the confirmed supplier pupil schema: UPPER_SNAKE field names mapped via
// [JsonPropertyName], with tolerant numeric binding (a value may arrive as a JSON
// number or a quoted string). Deserializes through PupilDataBlobClient.JsonOptions
// so the tests bind exactly as production does.
public class PupilRecordDeserializationTests
{
    private static PupilRecord DeserializeSingle(string json)
    {
        var pupils = JsonSerializer.Deserialize<List<PupilRecord>>(json, PupilDataBlobClient.JsonOptions);
        Assert.NotNull(pupils);
        return Assert.Single(pupils!);
    }

    [Fact]
    public void Maps_every_supplier_field_to_its_clean_property()
    {
        const string json = """
        [
          {
            "Id": "11111111-1111-1111-1111-111111111111",
            "CheckingWindowId": "22222222-2222-2222-2222-222222222222",
            "UPN": "A860407000001B",
            "MATCHREF": 29601694,
            "CYPMD_ID": "000123",
            "SURNAME": "Smith",
            "FORENAME": "Alice",
            "SEX": "F",
            "DOB": "01/09/2010",
            "AGE": 15,
            "P_INCL": 401,
            "LAESTAB": "860/4070",
            "URN": 136309,
            "ENTRYDAT": "01/09/2021",
            "SENF": "N",
            "LANG1ST": "ENG",
            "ETHNIC": "WBRI",
            "ACTYRGRP": "11",
            "NEWMOBILE": 0,
            "P_INCL_DESC": "Included on roll"
          }
        ]
        """;

        var pupil = DeserializeSingle(json);

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), pupil.Id);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), pupil.CheckingWindowId);
        Assert.Equal("A860407000001B", pupil.Upn);
        Assert.Equal(29601694, pupil.MatchRef);
        Assert.Equal("000123", pupil.Cypmd_Id);
        Assert.Equal("Smith", pupil.Surname);
        Assert.Equal("Alice", pupil.Firstname);
        Assert.Equal("F", pupil.Sex);
        Assert.Equal("01/09/2010", pupil.DateOfBirth);
        Assert.Equal(15, pupil.Age);
        Assert.Equal(401, pupil.Pincl);
        Assert.Equal("860/4070", pupil.Laestab);
        Assert.Equal(136309L, pupil.Urn);
        Assert.Equal("01/09/2021", pupil.EntryDate);
        Assert.Equal("N", pupil.SenF);
        Assert.Equal("ENG", pupil.FirstLanguage);
        Assert.Equal("WBRI", pupil.Ethnicity);
        Assert.Equal("11", pupil.ActualYearGroup);
        Assert.False(pupil.NewMobile);
    }

    [Fact]
    public void Binds_the_real_supplier_sample()
    {
        // Verbatim supplier sample (lighter redactions): P_INCL is a *quoted* numeric string,
        // DOB/ENTRYDAT are full space-separated timestamps with 7 fractional digits (NOT ISO-8601
        // 'T', NOT dd/MM/yyyy), URN is a number, NEWMOBILE is 0. CheckingWindowId given as a real
        // Guid (the supplier confirmed it will be one).
        const string json = """
        [
          {
            "UPN": "X11111111111",
            "MATCHREF": 29601694,
            "CYPMD_ID": "00123",
            "SURNAME": "Smith",
            "FORENAME": "A B C D",
            "SEX": "F",
            "DOB": "2000-01-01 00:00:00.0000000",
            "AGE": 15,
            "P_INCL": "401",
            "LAESTAB": "2084223",
            "URN": 100624,
            "ENTRYDAT": "2000-01-01 00:00:00.0000000",
            "SENF": "N",
            "LANG1ST": "ENG",
            "ETHNIC": "MWBC",
            "ACTYRGRP": "11",
            "NEWMOBILE": 0,
            "Id": "a9dd11a0-b625-41d8-82db-6d32f941930b",
            "CheckingWindowId": "abcde123-0000-0000-0000-000000000000",
            "P_INCL_DESC": "Pupil on roll and included in key stage 4 (both NOR and results)."
          }
        ]
        """;

        var pupil = DeserializeSingle(json);

        Assert.Equal(Guid.Parse("a9dd11a0-b625-41d8-82db-6d32f941930b"), pupil.Id);
        Assert.Equal("X11111111111", pupil.Upn);
        Assert.Equal(29601694, pupil.MatchRef);
        Assert.Equal("A B C D", pupil.Firstname);
        Assert.Equal(401, pupil.Pincl);                              // quoted "401" -> int
        Assert.Equal("2000-01-01 00:00:00.0000000", pupil.DateOfBirth); // timestamp kept raw
        Assert.Equal("2000-01-01 00:00:00.0000000", pupil.EntryDate);   // timestamp kept raw
        Assert.Equal(100624L, pupil.Urn);                            // number -> long
        Assert.False(pupil.NewMobile);                               // 0 -> false
    }

    [Fact]
    public void Ignores_unknown_p_incl_desc_field_without_throwing()
    {
        // The design intentionally does not capture P_INCL_DESC; it must be ignored, not error.
        const string json = """
        [ { "FORENAME": "Bob", "P_INCL_DESC": "Anything at all" } ]
        """;

        var pupil = DeserializeSingle(json);

        Assert.Equal("Bob", pupil.Firstname);
    }

    [Theory]
    [InlineData("401", 401)]   // supplier may quote the numeric code
    [InlineData("401", 401, false)]
    public void Binds_pincl_whether_number_or_quoted_string(string rawJsonValue, int expected, bool quoted = true)
    {
        var literal = quoted ? $"\"{rawJsonValue}\"" : rawJsonValue;
        var json = $$"""[ { "P_INCL": {{literal}} } ]""";

        var pupil = DeserializeSingle(json);

        Assert.Equal(expected, pupil.Pincl);
    }

    [Fact]
    public void Binds_urn_from_quoted_string_as_well_as_number()
    {
        const string json = """[ { "URN": "136309" } ]""";

        var pupil = DeserializeSingle(json);

        Assert.Equal(136309L, pupil.Urn);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Binds_newmobile_from_numeric_zero_and_one(int raw, bool expected)
    {
        var json = $$"""[ { "NEWMOBILE": {{raw}} } ]""";

        var pupil = DeserializeSingle(json);

        Assert.Equal(expected, pupil.NewMobile);
    }

    [Fact]
    public void Binds_null_pincl_without_throwing()
    {
        // The schema declares P_INCL as ["string","null"]; a null must not fail the whole file.
        const string json = """[ { "P_INCL": null } ]""";

        var pupil = DeserializeSingle(json);

        Assert.Null(pupil.Pincl);
    }

    [Fact]
    public void Coalesces_null_string_fields_to_empty()
    {
        // CYPMD_ID (and other nullable strings) may be null per schema; downstream search calls
        // .StartsWith on them, so they must never bind to null.
        const string json = """[ { "CYPMD_ID": null, "SEX": null, "ETHNIC": null } ]""";

        var pupil = DeserializeSingle(json);

        Assert.Equal(string.Empty, pupil.Cypmd_Id);
        Assert.Equal(string.Empty, pupil.Sex);
        Assert.Equal(string.Empty, pupil.Ethnicity);
    }

    [Theory]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(414, true)]
    [InlineData(421, true)]
    [InlineData(431, true)]
    [InlineData(402, false)]
    [InlineData(404, false)]
    public void IsIncluded_follows_the_ks4_pincl_codes(int pincl, bool expected)
    {
        var pupil = DeserializeSingle($$"""[ { "P_INCL": {{pincl}} } ]""");

        Assert.Equal(expected, pupil.IsIncluded);
    }

    [Fact]
    public void IsIncluded_is_false_when_pincl_is_absent()
    {
        var pupil = DeserializeSingle("""[ { "SURNAME": "Smith" } ]""");

        Assert.False(pupil.IsIncluded);
    }

    [Fact]
    public void Identifier_is_the_upn_for_ks4()
    {
        var pupil = DeserializeSingle("""[ { "UPN": "A860407000001B" } ]""");

        Assert.Equal("A860407000001B", pupil.Identifier);
    }
}
