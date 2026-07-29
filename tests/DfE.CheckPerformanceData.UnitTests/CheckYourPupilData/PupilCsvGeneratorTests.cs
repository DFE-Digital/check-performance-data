using System.Text;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// The CSV generator is now a pure renderer over PupilTable, so it works for any window type.
public class PupilCsvGeneratorTests
{
    [Fact]
    public void Writes_the_header_row_then_one_row_per_pupil()
    {
        var table = new PupilTable(
            ["UPN", "Surname"],
            [["A860407000001B", "Smith"], ["A860407000002B", "Jones"]]);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        var expected = string.Concat(
            "UPN,Surname", Environment.NewLine,
            "A860407000001B,Smith", Environment.NewLine,
            "A860407000002B,Jones", Environment.NewLine);
        Assert.Equal(expected, csv);
    }

    [Fact]
    public void Escapes_values_containing_commas_and_quotes()
    {
        var table = new PupilTable(["Description"], [["Pupil on roll, included"], ["He said \"yes\""]]);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        Assert.Contains("\"Pupil on roll, included\"", csv);
        Assert.Contains("\"He said \"\"yes\"\"\"", csv);
    }

    [Fact]
    public void An_empty_table_still_writes_its_headers()
    {
        var table = new PupilTable(["UPN", "Surname"], []);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        Assert.Equal("UPN,Surname" + Environment.NewLine, csv);
    }
}
