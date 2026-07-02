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
    public void ToDisplayString_IncompleteOrInvalidDate_ReturnsEmpty(int day, int month, int year)
    {
        var date = new DateAnswer { Day = day, Month = month, Year = year };

        Assert.Equal(string.Empty, date.ToDisplayString());
    }

    [Theory]
    [InlineData(12, 3, 2025, true)]
    [InlineData(29, 2, 2024, true)]   // leap day
    [InlineData(29, 2, 2025, false)]  // not a leap year
    [InlineData(0, 0, 0, false)]
    public void IsCompleteDate_ReflectsWhetherPartsFormARealDate(int day, int month, int year, bool expected)
    {
        var date = new DateAnswer { Day = day, Month = month, Year = year };

        Assert.Equal(expected, date.IsCompleteDate);
    }
}
