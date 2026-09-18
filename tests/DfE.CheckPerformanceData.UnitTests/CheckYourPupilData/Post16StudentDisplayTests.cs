using System.Text.Json;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

public class Post16StudentDisplayTests
{
    [Fact]
    public void Uses_each_uploaded_schema_for_table_search_and_download()
    {
        var included = Definition("students-included", true, "Last name", false);
        var nonIncluded = Definition("students-non-included", false, "Family name", true);
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["INCLUDED"] = "True", ["SURNAME"] = "Watkins", ["LAESTAB"] = "1234567" },
            new() { ["INCLUDED"] = "False", ["SURNAME"] = "Konsa", ["LAESTAB"] = "7654321" }
        };

        var view = Post16StudentDisplay.Build(rows, [included, nonIncluded],
            "students-non-included", "kon", 0, 10);

        Assert.Equal(["Family name", "DfE number"], view.Selected.Columns.Select(c => c.Label));
        Assert.Single(view.Rows);
        Assert.Equal("Konsa", view.Rows[0]["SURNAME"]);
        var csv = System.Text.Encoding.UTF8.GetString(Post16StudentDisplay.Csv(view.Selected));
        Assert.StartsWith("Surname,DfE number", csv);
        Assert.Contains("Konsa,7654321", csv);
        Assert.DoesNotContain(included.Columns, c => c.Field == "LAESTAB");
    }

    [Fact]
    public void Replacing_an_uploaded_schema_changes_the_display_definition()
    {
        var oldDefinition = Definition("students-non-included", false, "Last name", false);
        var newDefinition = Definition("students-non-included", false, "Family name", true);

        Assert.Equal("Last name", oldDefinition.Columns[0].Label);
        Assert.Equal(["Family name", "DfE number"], newDefinition.Columns.Select(c => c.Label));
    }

    [Fact]
    public void Reads_combined_envelope_without_losing_dataset_provenance()
    {
        using var json = JsonDocument.Parse("""
            {"students-included":[{"SURNAME":"Watkins"}],"students-non-included":[{"SURNAME":"Konsa"}],"results-included":[{"SURNAME":"Watkins","GNUMBER":"123"}]}
            """);
        var rows = Post16StudentDisplay.ReadRows(json.RootElement);
        var definitions = new[]
        {
            Definition("students-included", true, "Last name", false),
            Definition("students-non-included", false, "Last name", false)
        };

        Assert.Single(Post16StudentDisplay.Build(rows, definitions, "students-included", null, 0, 10).Rows);
        Assert.Single(Post16StudentDisplay.Build(rows, definitions, "students-non-included", null, 0, 10).Rows);
    }

    [Fact]
    public void Uses_ingress_inclusion_marker_when_all_schema_fields_are_present()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["INCLUDED"] = "True", ["SURNAME"] = "Watkins", ["LAESTAB"] = "123" },
            new() { ["INCLUDED"] = "False", ["SURNAME"] = "Konsa", ["LAESTAB"] = "123" }
        };
        var definitions = new[]
        {
            Definition("students-included", true, "Last name", false),
            Definition("students-non-included", false, "Last name", false)
        };

        Assert.Single(Post16StudentDisplay.Build(rows, definitions, "students-included", null, 0, 10).Rows);
        Assert.Single(Post16StudentDisplay.Build(rows, definitions, "students-non-included", null, 0, 10).Rows);
    }

    private static StudentDataset Definition(string key, bool included, string surnameLabel, bool showLaestab)
    {
        var schema = $$"""
            {
              "x-ingress": { "collection": "{{key}}" },
              "x-display": { "section": "{{key}}" },
              "x-download": { "fileName": "{{key}}.csv" },
              "properties": {
                "SURNAME": {
                  "x-display": { "label": "{{surnameLabel}}", "visible": true, "order": 0, "searchable": true },
                  "x-csv": { "columns": { "default": "A" }, "heading": "Surname" }
                },
                "LAESTAB": {
                  "x-display": { "label": "DfE number", "visible": {{showLaestab.ToString().ToLowerInvariant()}}, "order": 1 },
                  "x-csv": { "columns": { "default": "B" }, "heading": "DfE number" }
                }
              }
            }
            """;
        using var json = JsonDocument.Parse(schema);
        return Post16StudentDisplay.ParseDefinition(key, included, json.RootElement);
    }
}
