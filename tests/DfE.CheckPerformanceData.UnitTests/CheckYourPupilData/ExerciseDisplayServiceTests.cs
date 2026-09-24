using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

public class ExerciseDisplayServiceTests
{
    private static ExerciseDisplayService Service() => new();

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

        var view = Service().BuildTable(rows, [included, nonIncluded],
            "students-non-included", "kon", 0, 10);

        Assert.Equal(["Family name", "DfE number"], view.Selected.Columns.Select(c => c.Label));
        Assert.Single(view.Rows);
        Assert.Equal("Konsa", view.Rows[0]["SURNAME"]);
        var csv = System.Text.Encoding.UTF8.GetString(Service().Csv(view.Selected));
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
        var rows = Service().ReadRows(json.RootElement);
        var definitions = new[]
        {
            Definition("students-included", true, "Last name", false),
            Definition("students-non-included", false, "Last name", false)
        };

        Assert.Single(Service().BuildTable(rows, definitions, "students-included", null, 0, 10).Rows);
        Assert.Single(Service().BuildTable(rows, definitions, "students-non-included", null, 0, 10).Rows);
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

        Assert.Single(Service().BuildTable(rows, definitions, "students-included", null, 0, 10).Rows);
        Assert.Single(Service().BuildTable(rows, definitions, "students-non-included", null, 0, 10).Rows);
    }

    [Fact]
    public void Slots_that_share_a_schema_present_as_one_dataset()
    {
        // A results enquiry has one slot per supplier file (main, late, revised...) but every file
        // has the same shape and lands in the same per-school blob, so the page shows one dataset.
        var main = Definition("results-included", true, "Last name", false);
        var late = Definition("results-included", true, "Last name", false);
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["SURNAME"] = "Watkins", ["SOURCE"] = "16to19_MAIN" },
            new() { ["SURNAME"] = "Konsa", ["SOURCE"] = "16to19_LR1" }
        };

        var view = Service().BuildTable(rows, [main, late], null, null, 0, 10);

        var only = Assert.Single(view.Datasets);
        Assert.Equal("results-included", only.Key);
        Assert.Equal(2, view.Rows.Count);
    }

    [Fact]
    public void A_row_that_ties_between_two_schemas_is_shown_by_neither()
    {
        // No DATASET, no SURNAME_0, no INCLUDED marker to break the tie by name or inclusion, and
        // both schemas share the same field names, so both score the row's two populated fields
        // equally. Guessing here would put a pupil's record under the wrong dataset.
        var first = Definition("students-a", true, "Last name", true);
        var second = Definition("students-b", true, "Last name", true);
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["SURNAME"] = "Watkins", ["LAESTAB"] = "1234567" }
        };

        var datasets = Service().Define(rows, [first, second]);

        Assert.All(datasets, d => Assert.Empty(d.Rows));
    }

    [Fact]
    public void Vertical_view_lists_visible_fields_in_order_with_the_records_values()
    {
        var definition = SummaryDefinition("vertical");
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["LAESTAB"] = "8412009", ["TALLPUP_1618"] = "48", ["TALEVPUP_1618"] = "" }
        };

        var view = Service().BuildVertical(rows, definition);

        Assert.Equal("Summary", view.Label);
        Assert.Equal("summary.csv", view.FileName);
        Assert.Equal(
            [("Number of students", "48"), ("A level students", ""), ("Average point score", "")],
            view.Fields.Select(f => (f.Label, f.Value)));
    }

    [Fact]
    public void Vertical_view_uses_the_first_record_when_a_school_has_more_than_one()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["TALLPUP_1618"] = "48" },
            new() { ["TALLPUP_1618"] = "99" }
        };

        var view = Service().BuildVertical(rows, SummaryDefinition("vertical"));

        Assert.Equal("48", view.Fields[0].Value);
    }

    [Fact]
    public void Vertical_view_has_no_fields_when_a_school_has_no_record()
    {
        var view = Service().BuildVertical([], SummaryDefinition("vertical"));

        Assert.Empty(view.Fields);
    }

    [Fact]
    public void Csv_columns_fall_back_to_the_sheets_own_variant_when_there_is_no_default()
    {
        // The students sheets key their columns "provisional-revised"/"retention" with no
        // "default"; a schema may use any workbook variant names.
        var definition = SummaryDefinition("vertical");
        var csv = System.Text.Encoding.UTF8.GetString(Service().Csv(
            definition with { Rows = [new Dictionary<string, string> { ["LAESTAB"] = "8412009", ["TALLPUP_1618"] = "48" }] }));

        Assert.StartsWith("DfE number,Students", csv);
        Assert.Contains("8412009,48", csv);
    }

    private static ExerciseDataset SummaryDefinition(string layout)
    {
        var schema = $$"""
            {
              "x-ingress": { "collection": "summary" },
              "x-display": { "section": "Summary", "layout": "{{layout}}" },
              "x-download": { "fileName": "summary.csv" },
              "properties": {
                "LAESTAB": { "x-display": { "label": "DfE number", "visible": false },
                             "x-csv": { "columns": { "autumn": "A" }, "heading": "DfE number" } },
                "TAPS_1618": { "x-display": { "label": "Average point score", "order": 2 },
                               "x-csv": { "columns": {} } },
                "TALLPUP_1618": { "x-display": { "label": "Number of students", "order": 0 },
                                  "x-csv": { "columns": { "autumn": "B", "retention": "C" }, "heading": "Students" } },
                "TALEVPUP_1618": { "x-display": { "label": "A level students", "order": 1 } }
              }
            }
            """;
        using var json = JsonDocument.Parse(schema);
        return Service().ParseDefinition("summary", null, json.RootElement);
    }

    private static ExerciseDataset Definition(string key, bool included, string surnameLabel, bool showLaestab)
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
        return Service().ParseDefinition(key, included, json.RootElement);
    }
}
