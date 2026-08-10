using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class DateAnswerTests
{
    [Fact]
    public void ToDisplayString_ValidDate_FormatsAsDayFullMonthYear()
    {
        var date = new DateAnswer { Day = 12, Month = 3, Year = 2025 };

        Assert.Equal("12 March 2025", date.ToDisplayString());
    }

    [Theory]
    [InlineData(0, 0, 0)]      // blank optional date stored as zeroes
    [InlineData(0, 3, 2025)]   // missing day
    [InlineData(12, 0, 2025)]  // missing month
    [InlineData(12, 3, 0)]     // missing year
    [InlineData(31, 2, 2025)]  // impossible calendar date (31 February)
    [InlineData(12, 13, 2025)] // month out of range
    [InlineData(15, 6, 26)]    // two-digit year — must render empty, never "15 June 0026"
    public void ToDisplayString_IncompleteOrInvalidDate_ReturnsEmpty(int day, int month, int year)
    {
        var date = new DateAnswer { Day = day, Month = month, Year = year };

        Assert.Equal(string.Empty, date.ToDisplayString());
    }

    [Fact]
    public void ToDateOnly_ValidDate_ReturnsTheDate()
    {
        var date = new DateAnswer { Day = 12, Month = 3, Year = 2025 };

        Assert.Equal(new DateOnly(2025, 3, 12), date.ToDateOnly());
    }

    [Theory]
    [InlineData(0, 0, 0)]      // blank optional date
    [InlineData(12, 0, 2025)]  // part-filled
    [InlineData(31, 2, 2025)]  // impossible calendar date
    [InlineData(15, 6, 26)]    // two-digit year
    public void ToDateOnly_IncompleteOrInvalidDate_ReturnsNull(int day, int month, int year)
    {
        var date = new DateAnswer { Day = day, Month = month, Year = year };

        Assert.Null(date.ToDateOnly());
    }

    [Theory]
    [InlineData(12, 3, 2025, true)]
    [InlineData(29, 2, 2024, true)]   // leap day
    [InlineData(29, 2, 2025, false)]  // not a leap year
    [InlineData(0, 0, 0, false)]
    [InlineData(15, 6, 26, false)]    // two-digit year is not a complete answer
    [InlineData(15, 6, 999, false)]
    [InlineData(15, 6, 1000, true)]
    public void IsCompleteDate_ReflectsWhetherPartsFormARealDate(int day, int month, int year, bool expected)
    {
        var date = new DateAnswer { Day = day, Month = month, Year = year };

        Assert.Equal(expected, date.IsCompleteDate);
    }
}
