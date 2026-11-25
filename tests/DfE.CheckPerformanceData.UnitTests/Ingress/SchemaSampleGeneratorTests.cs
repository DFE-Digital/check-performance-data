using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

// #940, #941: the generic generator fills a file from its schema, and a school's results belong to
// its pupils.
public sealed class SchemaSampleGeneratorTests
{
    private static readonly SampleSchool School = new("9334290", "123456");

    private static SampleSchemaShape Shape(string schema) => SampleSchemaShape.From(JSchema.Parse(schema));

    private const string PupilSchema = """
    {
      "type": "object",
      "x-ingress": { "inclusion": { "field": "P_INCL", "included": ["401"], "descriptions": { "401": "In", "402": "Out" } } },
      "properties": {
        "CYPMD_ID": { "type": ["string", "null"] },
        "SURNAME": { "type": ["string", "null"], "maxLength": 3 },
        "DOB": { "type": ["string", "null"], "pattern": "^[0-9]{2}/[0-9]{2}/[0-9]{4}$" },
        "NEWMOBILE": { "type": ["integer", "null"], "enum": [1, null] },
        "P_INCL": { "type": ["string", "null"] },
        "INCLUDED": { "type": "boolean" }
      }
    }
    """;

    private const string ResultSchema = """
    { "type": "object", "properties": { "CYPMD_ID": { "type": ["string", "null"] }, "QAN": { "type": ["string", "null"] }, "GRADE": { "type": ["string", "null"] } } }
    """;

    private static SchemaSampleGenerator Generator(SampleRowKind kind, int firstPupil = 0) =>
        new("test", kind, 2009, SampleQualification.Gcses, firstPupil);

    [Fact]
    public void Each_result_belongs_to_a_pupil_of_the_pupil_file()
    {
        var pupils = Generator(SampleRowKind.Pupils).Rows(School, Shape(PupilSchema)).Select(r => r["CYPMD_ID"]).ToHashSet();
        var results = Generator(SampleRowKind.Results).Rows(School, Shape(ResultSchema)).ToList();

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains(r["CYPMD_ID"], pupils));
        Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r["QAN"])));
    }

    [Fact]
    public void A_file_that_starts_at_another_pupil_has_no_pupil_of_the_first_file()
    {
        var first = Generator(SampleRowKind.Pupils).Rows(School, Shape(PupilSchema)).Select(r => r["CYPMD_ID"]);
        var other = Generator(SampleRowKind.Pupils, firstPupil: 100).Rows(School, Shape(PupilSchema)).Select(r => r["CYPMD_ID"]);

        Assert.Empty(first.Intersect(other));
    }

    [Fact]
    public void Values_keep_the_schemas_limits_and_leave_the_stamps_to_ingress()
    {
        var rows = Generator(SampleRowKind.Pupils).Rows(School, Shape(PupilSchema)).ToList();

        Assert.All(rows, r =>
        {
            Assert.True(r["SURNAME"].Length <= 3);
            Assert.Equal(string.Empty, r["DOB"]);
            Assert.Equal("1", r["NEWMOBILE"]);
            Assert.Contains(r["P_INCL"], new[] { "401", "402" });
            Assert.False(r.ContainsKey("INCLUDED"));
        });
        Assert.Contains(rows, r => r["P_INCL"] == "401");
        Assert.Contains(rows, r => r["P_INCL"] == "402");
    }

    [Fact]
    public void A_summary_has_one_row()
    {
        Assert.Single(Generator(SampleRowKind.Summary).Rows(School, Shape(ResultSchema)));
    }
}
