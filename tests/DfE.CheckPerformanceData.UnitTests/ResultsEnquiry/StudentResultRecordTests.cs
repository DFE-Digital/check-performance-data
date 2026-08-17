using System.Text.Json;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#296648 / AB#296999: the per-student result read model binds the ingestion JSON through the
// same JsonSerializerOptions production uses, so a schema drift breaks here rather than in a
// running service. Unknown fields are ignored (ingestion may add columns) and numeric-looking
// values are tolerated whether the CSV-to-JSON step quoted them or not.
public sealed class StudentResultRecordTests
{
    // Record 1 quotes every value; record 2 leaves the numeric-looking ones unquoted and adds an
    // unknown "AWARDING_BODY" column — both must bind.
    private const string SampleJson = """
    [
      {
        "CYPMD_ID": "1606464434",
        "QAN": "6037116X",
        "QUAL_NAME": "GCSE (9-1) Bus. Studs:Single",
        "SYLLABUS": "1BS0",
        "SESSION": "S2024",
        "GRADE": "5",
        "SOURCE": "16to19_MAIN"
      },
      {
        "CYPMD_ID": 1606464434,
        "QAN": 60181576,
        "QUAL_NAME": "GCSE (9-1) French",
        "SYLLABUS": "1FR0",
        "SESSION": "S2024",
        "GRADE": 7,
        "SOURCE": "16to19_LR1",
        "AWARDING_BODY": "Pearson"
      }
    ]
    """;

    private static IReadOnlyList<StudentResultRecord> Deserialize()
        => JsonSerializer.Deserialize<List<StudentResultRecord>>(SampleJson, StudentResultsBlobClient.JsonOptions)!;

    [Fact]
    public void Binds_every_field_from_the_ingestion_schema()
    {
        var record = Deserialize()[0];

        Assert.Equal("1606464434", record.CypmdId);
        Assert.Equal("6037116X", record.Qan);
        Assert.Equal("GCSE (9-1) Bus. Studs:Single", record.QualificationName);
        Assert.Equal("1BS0", record.SyllabusCode);
        Assert.Equal("S2024", record.Session);
        Assert.Equal("5", record.Grade);
        Assert.Equal(ResultsFileTags.Post16Main, record.SourceFile);
    }

    [Fact]
    public void Ignores_unknown_columns_so_ingestion_can_add_fields()
    {
        var records = Deserialize();

        Assert.Equal(2, records.Count);
        Assert.Equal("GCSE (9-1) French", records[1].QualificationName);
    }

    [Fact]
    public void Tolerates_unquoted_numeric_values()
    {
        var record = Deserialize()[1];

        Assert.Equal("1606464434", record.CypmdId);
        Assert.Equal("60181576", record.Qan);
        Assert.Equal("7", record.Grade);
    }

    [Fact]
    public void Reads_a_null_string_as_empty_so_search_never_dereferences_null()
    {
        const string json = """[{ "CYPMD_ID": "1", "QAN": null, "GRADE": null }]""";

        var record = JsonSerializer.Deserialize<List<StudentResultRecord>>(json, StudentResultsBlobClient.JsonOptions)![0];

        Assert.Equal(string.Empty, record.Qan);
        Assert.Equal(string.Empty, record.Grade);
    }

    [Fact]
    public void Malformed_json_throws_so_a_corrupt_file_surfaces()
        => Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<List<StudentResultRecord>>("{not json", StudentResultsBlobClient.JsonOptions));

    [Fact]
    public void Source_tags_are_verbatim_from_the_ingestion_contract()
    {
        Assert.Equal("16to19_MAIN", ResultsFileTags.Post16Main);
        Assert.Equal("16to19_LR1", ResultsFileTags.Post16LateResults1);
        Assert.Equal("16to19_LR2", ResultsFileTags.Post16LateResults2);
        Assert.Equal("16to19_Revised", ResultsFileTags.Post16Revised);
        Assert.Equal("16to19_Retention", ResultsFileTags.Post16Retention);
        Assert.Equal("KS4_MAIN", ResultsFileTags.Ks4Main);
        Assert.Equal("KS4_LR1", ResultsFileTags.Ks4LateResults1);
        Assert.Equal("KS4_LR2", ResultsFileTags.Ks4LateResults2);
        Assert.Equal("KS4_Revised", ResultsFileTags.Ks4Revised);
    }

    [Fact]
    public void Blob_path_is_scoped_to_the_results_enquiry_activity()
    {
        // docs/16-19-window-model.md consequence #2: each activity owns its blob prefix, so a
        // per-activity ingress sweep cannot destroy another activity's output.
        Assert.Equal("results-enquiry/data/", ResultsEnquiryBlobPaths.ResultsPrefix);
        Assert.Equal("results-enquiry/data/9334070_results.json", ResultsEnquiryBlobPaths.ResultsBlobName("933/4070"));
    }
}
