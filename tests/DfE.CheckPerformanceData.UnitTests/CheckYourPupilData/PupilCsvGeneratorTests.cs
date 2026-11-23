using System.Text;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

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

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    [InlineData("\ncmd", "\"'\ncmd\"")]
    [InlineData("\uFF1D1+1", "'\uFF1D1+1")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    public void A_value_that_a_spreadsheet_reads_as_a_formula_gets_an_apostrophe(string value, string cell)
    {
        var table = new PupilTable(["Notes"], [[value]]);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        Assert.Equal(string.Concat("Notes", Environment.NewLine, cell, Environment.NewLine), csv);
    }

    [Theory]
    [InlineData("-1.00")]
    [InlineData("+0.35")]
    [InlineData("-1.5E-07")]
    public void A_signed_number_is_the_same_in_the_file(string number)
    {
        var table = new PupilTable(["Progress score"], [[number]]);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        Assert.Equal(string.Concat("Progress score", Environment.NewLine, number, Environment.NewLine), csv);
    }

    [Fact]
    public void A_heading_that_a_spreadsheet_reads_as_a_formula_gets_an_apostrophe()
    {
        var table = new PupilTable(["=1+1", "Surname"], [["x", "Smith"]]);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        Assert.Equal(string.Concat("'=1+1,Surname", Environment.NewLine, "x,Smith", Environment.NewLine), csv);
    }

    [Theory]
    [InlineData("-1.10 (-2.35 to 0.15)")]
    [InlineData("+")]
    [InlineData("-5%")]
    public void A_signed_value_in_a_supplier_form_is_the_same_in_the_file(string value)
    {
        var table = new PupilTable(["Progress score"], [[value]]);

        string csv = Encoding.UTF8.GetString(PupilCsvGenerator.Generate(table));

        Assert.Equal(string.Concat("Progress score", Environment.NewLine, value, Environment.NewLine), csv);
    }
}
