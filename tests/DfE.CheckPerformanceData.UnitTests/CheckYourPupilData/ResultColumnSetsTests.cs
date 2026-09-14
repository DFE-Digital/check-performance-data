using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Results;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// The Results tab shows one row per main-file result, joined to the school's pupil file on CYPMD
// ID. The demographic cells come from the pupil; Subject and the CSV-only cells from the result.
public class ResultColumnSetsTests
{
    private static Post16PupilRecord Pupil() => new()
    {
        Id = Guid.NewGuid(),
        Included = true,
        Cypmd_Id = "500123",
        Surname = "Jones",
        Firstname = "Bob",
        Sex = "M",
        DateOfBirth = "2007-09-01 00:00:00.0000000",
        Age = 18,
        Pincl = 501,
        Laestab = "860/4070",
        Urn = "136309",
        Ukprn = "10001234",
        Uln = "9900112233"
    };

    private static StudentResultRecord Result() => new()
    {
        CypmdId = "500123",
        Qan = "60145642",
        QualificationName = "GCSE (9-1) Bus. Studs:Single",
        SyllabusCode = "1BS0",
        Session = "S2024",
        Grade = "5",
        SourceFile = ResultsFileTags.Post16Main
    };

    [Fact]
    public void Table_columns_are_the_agreed_seven()
    {
        Assert.Equal(
            ["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID", "Subject"],
            ResultColumnSets.Table().Select(c => c.Header));
    }

    [Fact]
    public void Csv_columns_are_the_table_columns_plus_the_result_detail()
    {
        Assert.Equal(
            ["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID", "Subject", "QAN", "Session", "Grade", "Source file"],
            ResultColumnSets.Csv().Select(c => c.Header));
    }

    [Fact]
    public void Matched_row_renders_pupil_demographics_and_the_qualification_name_as_subject()
    {
        var table = ResultTable.Build(ResultColumnSets.Csv(), [new ResultRow(Pupil(), Result())]);

        var row = Assert.Single(table.Rows);
        Assert.Equal(["Jones", "Bob", "M", "01/09/2007", "18", "500123", "GCSE (9-1) Bus. Studs:Single", "60145642", "S2024", "5", ResultsFileTags.Post16Main], row);
    }

    [Fact]
    public void Unmatched_row_keeps_blank_demographics_and_the_result_cypmd_id()
    {
        var table = ResultTable.Build(ResultColumnSets.Table(), [new ResultRow(null, Result())]);

        var row = Assert.Single(table.Rows);
        Assert.Equal(["", "", "", "", "", "500123", "GCSE (9-1) Bus. Studs:Single"], row);
    }
}
