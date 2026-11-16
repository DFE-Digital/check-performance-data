using DfE.CheckPerformanceData.Infrastructure.Ingress;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class CodeDescriptionsTests
{
    private const string RulesPath = "x-ingress.codeDescriptions";
    private const string InclusionPath = "x-ingress.inclusion";

    private const string TwoRules = """
        [
          { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words for 51", " 52 ": "Words for 52" } },
          { "field": "QUAL_KS4", "descriptionField": "QUAL_KS4_WORDS", "descriptions": { "59": "Words for 59" } }
        ]
        """;

    private static JSchema Schema(string? rules)
    {
        var codeDescriptions = rules is null ? string.Empty : $", \"codeDescriptions\": {rules}";
        return JSchema.Parse($$"""
            {
              "type": "object",
              "x-ingress": { "collection": "results"{{codeDescriptions}} },
              "properties": {
                "R_INCL":         { "type": ["integer", "null"] },
                "R_INCL_WORDS":   { "type": ["string", "null"] },
                "QUAL_KS4":       { "type": ["integer", "null"] },
                "QUAL_KS4_WORDS": { "type": ["string", "null"] },
                "POINTS":         { "type": ["number", "null"] }
              }
            }
            """);
    }

    private static JObject Record(object? resultCode, object? qualificationCode = null) => new()
    {
        ["R_INCL"] = resultCode is null ? JValue.CreateNull() : JToken.FromObject(resultCode),
        ["R_INCL_WORDS"] = JValue.CreateNull(),
        ["QUAL_KS4"] = qualificationCode is null ? JValue.CreateNull() : JToken.FromObject(qualificationCode),
        ["QUAL_KS4_WORDS"] = JValue.CreateNull()
    };

    [Theory]
    [InlineData(51, "Words for 51")]
    [InlineData("51", "Words for 51")]
    [InlineData(" 51 ", "Words for 51")]
    [InlineData(52, "Words for 52")]
    [InlineData(99, "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_rule_writes_the_words_for_the_code_and_leaves_any_other_code_blank(object? code, string words)
    {
        var record = Record(code);

        CodeDescriptions.From(Schema(TwoRules)).Describe(record);

        Assert.Equal(words, record["R_INCL_WORDS"]!.ToString());
    }

    [Fact]
    public void Each_rule_describes_its_own_field()
    {
        var record = Record(51, 59);

        CodeDescriptions.From(Schema(TwoRules)).Describe(record);

        Assert.Equal("Words for 51", record["R_INCL_WORDS"]!.ToString());
        Assert.Equal("Words for 59", record["QUAL_KS4_WORDS"]!.ToString());
    }

    [Fact]
    public void A_description_the_record_already_holds_is_kept()
    {
        var record = Record(51);
        record["R_INCL_WORDS"] = "From the supplier";

        CodeDescriptions.From(Schema(TwoRules)).Describe(record);

        Assert.Equal("From the supplier", record["R_INCL_WORDS"]!.ToString());
    }

    [Fact]
    public void The_inclusion_rules_description_is_written_with_the_schemas_rules()
    {
        const string oneRule = """[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words for 51" } } ]""";
        var inclusion = new CodeDescriptionRule("QUAL_KS4", "QUAL_KS4_WORDS",
            new Dictionary<string, string> { ["59"] = "Words from the inclusion rule" });
        var schema = Schema(oneRule);
        var record = Record(51, 59);

        var descriptions = CodeDescriptions.From(schema, inclusion);
        descriptions.Describe(record);

        Assert.Null(descriptions.ErrorFor(schema));
        Assert.Equal("Words for 51", record["R_INCL_WORDS"]!.ToString());
        Assert.Equal("Words from the inclusion rule", record["QUAL_KS4_WORDS"]!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public void A_schema_with_no_rules_is_valid_and_writes_nothing(string? rules)
    {
        var schema = Schema(rules);
        var record = Record(51, 59);

        var descriptions = CodeDescriptions.From(schema);
        descriptions.Describe(record);

        Assert.Null(descriptions.ErrorFor(schema));
        Assert.Equal(string.Empty, record["R_INCL_WORDS"]!.ToString());
    }

    [Fact]
    public void Valid_rules_have_no_error()
    {
        var schema = Schema(TwoRules);

        Assert.Null(CodeDescriptions.From(schema).ErrorFor(schema));
    }

    [Theory]
    [InlineData("""null""", RulesPath)]
    [InlineData("""{ "field": "R_INCL" }""", RulesPath)]
    [InlineData("""[ "R_INCL" ]""", RulesPath)]
    [InlineData("""[ { "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words" } } ]""", RulesPath)]
    [InlineData("""[ { "field": "NOT_DECLARED", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words" } } ]""", "NOT_DECLARED")]
    [InlineData("""[ { "field": "R_INCL", "descriptions": { "51": "Words" } } ]""", "R_INCL")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "NOT_DECLARED", "descriptions": { "51": "Words" } } ]""", "NOT_DECLARED")]
    [InlineData("""[ { "field": "R_INCL_WORDS", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words" } } ]""", "R_INCL_WORDS")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "POINTS", "descriptions": { "51": "Words" } } ]""", "POINTS")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS" } ]""", "R_INCL")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { " ": "Words" } } ]""", "R_INCL")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words" } }, { "field": "QUAL_KS4", "descriptionField": "R_INCL_WORDS", "descriptions": { "59": "Words" } } ]""", "R_INCL_WORDS")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words", " 51 ": "Other words" } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": 7 } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "" } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": { "words": "Words" } } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words" } }, { "field": "R_INCL_WORDS", "descriptionField": "QUAL_KS4_WORDS", "descriptions": { "Words": "More words" } } ]""", "R_INCL_WORDS")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": null } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "  " } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "=1+1" } } ]""", "51")]
    [InlineData("""[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words", "52": "@Words" } } ]""", "52")]
    public void Rules_the_schema_cannot_honour_have_an_error_that_names_the_key(string rules, string named)
    {
        var schema = Schema(rules);

        var error = CodeDescriptions.From(schema).ErrorFor(schema);

        Assert.NotNull(error);
        Assert.Contains(RulesPath, error);
        Assert.Contains(named, error);
    }

    [Fact]
    public void A_rule_that_writes_the_inclusion_rules_description_field_has_an_error()
    {
        const string oneRule = """[ { "field": "R_INCL", "descriptionField": "R_INCL_WORDS", "descriptions": { "51": "Words" } } ]""";
        var inclusion = new CodeDescriptionRule("QUAL_KS4", "R_INCL_WORDS", new Dictionary<string, string> { ["59"] = "Words" });
        var schema = Schema(oneRule);

        var error = CodeDescriptions.From(schema, inclusion).ErrorFor(schema);

        Assert.NotNull(error);
        Assert.Contains("R_INCL_WORDS", error);
        Assert.Contains(InclusionPath, error);
    }

    [Fact]
    public void A_rule_that_writes_the_field_the_inclusion_rule_reads_has_an_error()
    {
        const string oneRule = """[ { "field": "R_INCL", "descriptionField": "QUAL_KS4_WORDS", "descriptions": { "51": "Words" } } ]""";
        var inclusion = new CodeDescriptionRule("QUAL_KS4_WORDS", "R_INCL_WORDS", new Dictionary<string, string> { ["Words"] = "More words" });
        var schema = Schema(oneRule);

        var error = CodeDescriptions.From(schema, inclusion).ErrorFor(schema);

        Assert.NotNull(error);
        Assert.Contains("QUAL_KS4_WORDS", error);
        Assert.Contains(InclusionPath, error);
    }

    [Fact]
    public void A_rule_that_reads_the_field_the_inclusion_rule_writes_has_an_error()
    {
        const string oneRule = """[ { "field": "R_INCL_WORDS", "descriptionField": "QUAL_KS4_WORDS", "descriptions": { "Words": "More words" } } ]""";
        var inclusion = new CodeDescriptionRule("QUAL_KS4", "R_INCL_WORDS", new Dictionary<string, string> { ["59"] = "Words" });
        var schema = Schema(oneRule);

        var error = CodeDescriptions.From(schema, inclusion).ErrorFor(schema);

        Assert.NotNull(error);
        Assert.Contains("R_INCL_WORDS", error);
        Assert.Contains(InclusionPath, error);
    }
}
