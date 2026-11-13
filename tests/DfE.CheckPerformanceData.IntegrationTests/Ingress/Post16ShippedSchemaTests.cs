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
}
