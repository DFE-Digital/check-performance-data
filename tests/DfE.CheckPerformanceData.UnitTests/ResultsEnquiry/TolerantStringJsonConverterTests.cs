using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class TolerantStringJsonConverterTests
{
    private static readonly JsonSerializerOptions Opts = ResultsEnquiryJson.Options;

    // Helper: deserializes a JSON object with a single "v" property through the production converter.
    private static string ReadValue(string json)
        => JsonSerializer.Deserialize<Wrapper>(json, Opts)!.V;

    [Fact]
    public void Reads_a_string_value_as_is()
    {
        var result = ReadValue("""{ "v": "hello" }""");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Reads_null_as_empty_string()
    {
        var result = ReadValue("""{ "v": null }""");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Reads_a_number_as_raw_utf8_bytes()
    {
        var result = ReadValue("""{ "v": 123 }""");
        Assert.Equal("123", result);
    }

    [Fact]
    public void Reads_true_as_bool_true_string()
    {
        var result = ReadValue("""{ "v": true }""");
        Assert.Equal(bool.TrueString, result);
    }

    [Fact]
    public void Reads_false_as_bool_false_string()
    {
        var result = ReadValue("""{ "v": false }""");
        Assert.Equal(bool.FalseString, result);
    }

    [Fact]
    public void Throws_on_unexpected_token_type()
    {
        // "v" points at a JSON array — TolerantStringJsonConverter does not handle arrays.
        Assert.Throws<JsonException>(() => ReadValue("""{ "v": [1,2] }"""));
    }

    [Fact]
    public void Write_produces_valid_json_string()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            JsonSerializer.Serialize(writer, "hello world", Opts);
        }

        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("\"hello world\"", json);
    }

    // --- FR-005: composite key identity ---

    [Fact]
    public void Trims_leading_and_trailing_whitespace_from_string_fields()
    {
        const string json = """
        [
          {
            "CYPMD_ID": "1",
            "QAN": "  6037116X  ",
            "QUAL_NAME": "  GCSE (9-1) Bus. Studs  ",
            "SYLLABUS": "  1BS0  ",
            "SESSION": "  S2024  ",
            "GRADE": "  5  ",
            "SOURCE": "  16to19_MAIN  "
          }
        ]
        """;

        var records = JsonSerializer.Deserialize<List<StudentResultRecord>>(json, StudentResultsBlobClient.JsonOptions)!;
        var record = records[0];

        Assert.Equal("6037116X", record.Qan);
        Assert.Equal("GCSE (9-1) Bus. Studs", record.QualificationName);
        Assert.Equal("1BS0", record.SyllabusCode);
        Assert.Equal("S2024", record.Session);
        Assert.Equal("5", record.Grade);
        Assert.Equal("16to19_MAIN", record.SourceFile);
    }

    [Fact]
    public void CompositeKey_uses_trimmed_values()
    {
        const string json = """
        [
          {
            "CYPMD_ID": "1",
            "QAN": "  6037116X  ",
            "QUAL_NAME": "  GCSE  ",
            "SYLLABUS": "  1BS0  ",
            "SESSION": "  S2024  ",
            "GRADE": "  5  ",
            "SOURCE": "  16to19_MAIN  "
          }
        ]
        """;

        var record = JsonSerializer.Deserialize<List<StudentResultRecord>>(json, StudentResultsBlobClient.JsonOptions)![0];

        Assert.Equal("6037116X|S2024|16to19_MAIN", record.CompositeKey);
    }

    private sealed class Wrapper
    {
        [JsonPropertyName("v")]
        public string V { get; init; } = string.Empty;
    }
}
