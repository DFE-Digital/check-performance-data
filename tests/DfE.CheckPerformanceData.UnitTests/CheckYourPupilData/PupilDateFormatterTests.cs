using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// The supplier's DOB/ENTRYDAT arrive as raw strings whose format varies (the schema promises
// ISO "YYYY-MM-DD" but sample data has "YYYY-MM-DD HH:mm:ss.fffffff"). PupilDateFormatter
// normalises whatever we receive to the UK display form dd/MM/yyyy, and never loses data it
// cannot parse.
public class PupilDateFormatterTests
{
    [Theory]
    [InlineData("2000-01-01", "01/01/2000")]                        // schema ISO date form
    [InlineData("2000-01-01 00:00:00.0000000", "01/01/2000")]       // supplier timestamp form
    [InlineData("2021-09-15T00:00:00", "15/09/2021")]               // ISO with T
    [InlineData("01/09/2021", "01/09/2021")]                        // already dd/MM/yyyy (dev seed)
    public void Formats_known_shapes_to_ddMMyyyy(string raw, string expected)
        => Assert.Equal(expected, PupilDateFormatter.ToDisplayDate(raw));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("unknown")]
    public void Returns_input_unchanged_when_unparseable(string raw)
        => Assert.Equal(raw, PupilDateFormatter.ToDisplayDate(raw));

    [Theory]
    [InlineData("2000-01-01", "2000-01-01")]                      // schema ISO date form
    [InlineData("2000-01-01 00:00:00.0000000", "2000-01-01")]     // supplier timestamp form
    [InlineData("01/09/2024", "2024-09-01")]                      // dd/MM/yyyy (dev seed) -> ISO
    [InlineData("", "")]
    [InlineData("not-a-date", "not-a-date")]
    public void ToIsoDate_normalises_known_shapes_and_returns_input_unchanged_otherwise(string raw, string expected)
        => Assert.Equal(expected, PupilDateFormatter.ToIsoDate(raw));

    [Theory]
    [InlineData("2000-01-01", "2000-01-01")]                      // schema ISO date form
    [InlineData("2000-01-01 00:00:00.0000000", "2000-01-01")]     // supplier timestamp form
    [InlineData("01/09/2024", "2024-09-01")]                      // dd/MM/yyyy (dev seed) -> ISO
    public void TryToIsoDate_normalises_known_shapes(string raw, string expected)
    {
        Assert.True(PupilDateFormatter.TryToIsoDate(raw, out var isoDate));
        Assert.Equal(expected, isoDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("01-09-2024")]                                    // supplier format Zendesk rejects
    [InlineData("2024/09/01")]                                    // supplier format Zendesk rejects
    public void TryToIsoDate_returns_false_when_unparseable(string raw)
        => Assert.False(PupilDateFormatter.TryToIsoDate(raw, out _));
}
