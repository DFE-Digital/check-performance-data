using System.Text.Json;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

public class Post16StudentDisplayTests
{
    [Fact]
    public void Uses_schema_columns_and_searchable_fields_for_each_student_dataset()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["P_INCL"] = "501", ["SURNAME"] = "Watkins", ["FORENAMES"] = "Ollie", ["DOB"] = "2009-01-01", ["CYPMD_ID"] = "1" },
            new() { ["SURNAME"] = "Konsa", ["FORENAMES"] = "Ezri", ["CYPMD_ID"] = "2" },
            new() { ["SURNAME_0"] = "Gallagher", ["FORENAMES_0"] = "Conor", ["CYPMD_ID"] = "3" }
        };

        var included = Post16StudentDisplay.Build(rows, "students-included", "oll", 0, 10);
        Assert.Equal(["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID"],
            included.Selected.Columns.Select(c => c.Label));
        Assert.Single(included.Rows);
        Assert.Equal("01/01/2009", Post16StudentDisplay.Value(included.Rows[0], "DOB"));

        var nonIncluded = Post16StudentDisplay.Build(rows, "students-non-included", null, 0, 10);
        Assert.Single(nonIncluded.Rows);
        Assert.Equal("Konsa", nonIncluded.Rows[0]["SURNAME"]);
        var csv = System.Text.Encoding.UTF8.GetString(Post16StudentDisplay.Csv(nonIncluded.Selected));
        Assert.StartsWith("CYPMD ID,Surname,Forename,Sex,Date of birth,Age", csv);
        Assert.Contains("Konsa,Ezri", csv);

        var previous = Post16StudentDisplay.Build(rows, "students-previously-published", "Con", 0, 10);
        Assert.Single(previous.Rows);
        Assert.Equal("Gallagher", previous.Rows[0]["SURNAME_0"]);
        Assert.DoesNotContain(previous.Selected.Columns, c => c.Field == "AGE");
    }

    [Fact]
    public void Reads_combined_envelope_without_losing_dataset_provenance()
    {
        using var json = JsonDocument.Parse("""
            {"students-included":[{"SURNAME":"Watkins"}],"students-non-included":[{"SURNAME":"Konsa"}],"results-included":[{"SURNAME":"Watkins","GNUMBER":"123"}]}
            """);
        var rows = Post16StudentDisplay.ReadRows(json.RootElement);
        Assert.Single(Post16StudentDisplay.Build(rows, "students-included", null, 0, 10).Rows);
        Assert.Single(Post16StudentDisplay.Build(rows, "students-non-included", null, 0, 10).Rows);
    }

    [Fact]
    public void Uses_ingress_inclusion_marker_when_all_schema_fields_are_present()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["INCLUDED"] = "True", ["SURNAME"] = "Watkins", ["FORENAMES"] = "Ollie", ["LearningAimReference"] = "" },
            new() { ["INCLUDED"] = "False", ["SURNAME"] = "Konsa", ["FORENAMES"] = "Ezri", ["P_INCL"] = "" }
        };

        Assert.Single(Post16StudentDisplay.Build(rows, "students-included", null, 0, 10).Rows);
        Assert.Single(Post16StudentDisplay.Build(rows, "students-non-included", null, 0, 10).Rows);
    }
}
