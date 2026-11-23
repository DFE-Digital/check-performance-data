using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

// #841: the 16-19 schemas the service ships give the CSV files the Autumn 2026 data
// requirements specification asks for. These read the shipped files, so a schema edit that
// breaks the file shape fails here. They pin structure, not wording.
public sealed class Post16ShippedSchemaTests
{
    private static ExerciseDataset Shipped(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "post16", file)));
        return new ExerciseDisplayService().ParseDefinition(file, null, json.RootElement);
    }

    private static List<string> CsvFields(string file) => Shipped(file).CsvColumns.Select(c => c.Field).ToList();

    [Fact]
    public void The_included_students_file_has_the_inclusion_description_straight_after_the_flag()
    {
        var fields = CsvFields("students-included_schema.json");

        Assert.Equal("P_INCL_DESC", fields[fields.IndexOf("P_INCL") + 1]);
    }

    [Theory]
    [InlineData("P_INCL_aims")]
    [InlineData("P_INCL_aims_DESC")]
    [InlineData("LearningAimReference")]
    [InlineData("Retention_CompletionStatus")]
    [InlineData("InScope_ALEV_Ret")]
    [InlineData("Ret_Assessed_TechCert")]
    public void The_included_students_file_leaves_out_a_field_shared_only_in_retention(string field)
    {
        Assert.DoesNotContain(field, CsvFields("students-included_schema.json"));
    }

    [Fact]
    public void The_included_students_file_has_the_columns_of_the_provisional_and_revised_file()
    {
        var fields = CsvFields("students-included_schema.json");

        // Columns A to BL of the specification.
        Assert.Equal(64, fields.Count);
        Assert.Equal("CYPMD_ID", fields[0]);
        Assert.Equal("KS4_Year_Calc", fields[^1]);
        Assert.Contains("TLEVEL", fields);
        Assert.Contains("ACH_L3M", fields);
    }

    [Fact]
    public void The_non_included_students_file_ends_with_the_two_campus_identifiers()
    {
        var fields = CsvFields("students-non-included_schema.json");

        Assert.Equal(12, fields.Count);
        Assert.Equal(["CampID_0", "CampID_1"], fields.TakeLast(2));
    }

    [Theory]
    [InlineData("students-included_schema.json", "students-non-included_schema.json")]
    [InlineData("students-non-included_schema.json", "students-previously-published_schema.json")]
    [InlineData("results-included_schema.json", "results-non-included_schema.json")]
    public void An_included_file_is_listed_before_the_files_that_follow_it(string first, string second)
    {
        Assert.True(Shipped(first).SelectorOrder < Shipped(second).SelectorOrder);
    }

    private static readonly string[] ResultFlags = ["R_INCL", "R_INCL_EM", "QUAL_KS4"];
    private const int ResultColumnCount = 33;

    [Theory]
    [InlineData("results-included_schema.json")]
    [InlineData("results-non-included_schema.json")]
    [InlineData("results-included-revised_schema.json")]
    [InlineData("results-non-included-revised_schema.json")]
    [InlineData("results-included-revised-retention_schema.json")]
    public void A_results_file_has_each_description_straight_after_its_flag(string file)
    {
        var fields = CsvFields(file);

        Assert.All(ResultFlags, flag => Assert.Equal($"{flag}_DESCRIPTION", fields[fields.IndexOf(flag) + 1]));
        Assert.Equal(Enumerable.Range(1, ResultColumnCount), Shipped(file).CsvColumns.Select(column => column.Order));
        Assert.Equal(["UKPRN", "URN", "LAESTAB"], fields.TakeLast(3));
    }

    private static readonly string[] ResultSchemaFiles =
    [
        "results-included_schema.json",
        "results-non-included_schema.json",
        "results-included-revised_schema.json",
        "results-non-included-revised_schema.json",
        "results-included-revised-retention_schema.json"
    ];

    private static readonly string[] FirstFlagToDfeNumber =
    [
        "R_INCL", "R_INCL_DESCRIPTION", "R_INCL_EM", "R_INCL_EM_DESCRIPTION", "QUAL_KS4", "QUAL_KS4_DESCRIPTION",
        "UKPRN", "URN", "LAESTAB"
    ];

    private static readonly string[] FirstFlagToDfeNumberLetters = ["Y", "Z", "AA", "AB", "AC", "AD", "AE", "AF", "AG"];

    private static T FromSchema<T>(string file, Func<JsonElement, T> read)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "post16", file)));
        return read(json.RootElement);
    }

    [Theory]
    [InlineData("results-included_schema.json")]
    [InlineData("results-non-included_schema.json")]
    [InlineData("results-included-revised_schema.json")]
    [InlineData("results-non-included-revised_schema.json")]
    [InlineData("results-included-revised-retention_schema.json")]
    public void A_results_file_has_the_columns_from_the_first_flag_to_the_DfE_number_at_Y_to_AG(string file)
    {
        var letters = FromSchema(file, root => FirstFlagToDfeNumber
            .Select(field => root.GetProperty("properties").GetProperty(field)
                .GetProperty("x-csv").GetProperty("columns").GetProperty("default").GetString())
            .ToList());

        Assert.Equal(FirstFlagToDfeNumberLetters, letters);
    }

    [Fact]
    public void The_five_results_files_have_the_same_code_descriptions()
    {
        var rules = ResultSchemaFiles
            .Select(file => FromSchema(file, root =>
                JsonSerializer.Serialize(root.GetProperty("x-ingress").GetProperty("codeDescriptions"))))
            .Distinct();

        Assert.Single(rules);
    }
}
