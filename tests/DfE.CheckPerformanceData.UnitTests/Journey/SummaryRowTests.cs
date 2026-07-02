using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class SummaryRowTests
{
    private static readonly JourneyPage Page = new() { Id = "english-not-first-language-details" };

    private static SummaryRow DateRow(DateAnswer? date) =>
        new(Page,
            new Question { Id = "date-pupil-arrived-in-england", Type = QuestionType.Date, Title = "When did they arrive?" },
            new QuestionAnswer { DateValue = date },
            "Date of arrival in England");

    // Bug 288617: an optional Date question left blank is stored as 0/0/0. Rendering the
    // summary previously called new DateTime(0, 0, 0), throwing ArgumentOutOfRangeException
    // and returning a 500 on GET /Journey/{id}/summary.
    [Fact]
    public void DisplayAnswer_BlankOptionalDate_ReturnsEmptyWithoutThrowing()
    {
        var row = DateRow(new DateAnswer { Day = 0, Month = 0, Year = 0 });

        Assert.Equal(string.Empty, row.DisplayAnswer);
    }

    [Fact]
    public void DisplayAnswer_ImpossibleCalendarDate_ReturnsEmptyWithoutThrowing()
    {
        var row = DateRow(new DateAnswer { Day = 31, Month = 2, Year = 2025 });

        Assert.Equal(string.Empty, row.DisplayAnswer);
    }

    [Fact]
    public void DisplayAnswer_ValidDate_FormatsAsDayFullMonthYear()
    {
        var row = DateRow(new DateAnswer { Day = 12, Month = 3, Year = 2025 });

        Assert.Equal("12 March 2025", row.DisplayAnswer);
    }
}
