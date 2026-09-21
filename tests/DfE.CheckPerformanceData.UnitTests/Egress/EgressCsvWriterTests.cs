using System.Text;
using DfE.CheckPerformanceData.Application.Egress;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// AB#292610 file rules: CSV; no trailing blank characters; no rows below the final data row; no
// trailing comma after the final heading; no extra columns. The writer is the one place those hold.
public sealed class EgressCsvWriterTests
{
    private static readonly IReadOnlyList<EgressColumn<string[]>> Columns =
    [
        new("A", r => r[0]),
        new("B", r => r[1])
    ];

    [Fact]
    public void Header_then_rows_joined_with_CRLF_and_no_trailing_line_break()
    {
        var bytes = EgressCsvWriter.Write(Columns, [["1", "2"], ["3", "4"]]);
        Assert.Equal("A,B\r\n1,2\r\n3,4", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Header_only_when_there_are_no_rows()
    {
        var bytes = EgressCsvWriter.Write(Columns, []);
        Assert.Equal("A,B", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Values_are_trimmed_and_quoted_only_when_they_need_it()
    {
        var bytes = EgressCsvWriter.Write(Columns, [[" plain ", "has,comma"], ["has \"quote\"", "line\nbreak"]]);
        Assert.Equal("A,B\r\nplain,\"has,comma\"\r\n\"has \"\"quote\"\"\",\"line\nbreak\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Utf8_without_a_byte_order_mark()
    {
        var bytes = EgressCsvWriter.Write(Columns, [["é", "x"]]);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("A,B\r\né,x", Encoding.UTF8.GetString(bytes));
    }
}
