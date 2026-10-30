using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class CohortStartDateFactTests
{
    private static readonly JourneyConditionContext Context = new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext()
    };

    [Fact]
    public void Name_IsCohortStart()
    {
        Assert.Equal("cohort.start", new CohortStartDateFact().Name);
    }

    /// <summary>
    /// Pins the boundary that the rules engine seeds for the same field
    /// (<c>schoolAdmissionDate gte 2022-09-01</c>). If the two differ, the journey accepts a
    /// request that the rules engine then rejects.
    /// </summary>
    [Theory]
    [InlineData(2026, 6, 15, 2022)]
    [InlineData(2026, 1, 5, 2022)]
    [InlineData(2027, 6, 15, 2023)]
    public void Value_IsFirstOfSeptemberFourYearsBack(int year, int month, int day, int expectedYear)
    {
        Assert.Equal(new DateOnly(expectedYear, 9, 1),
            new CohortStartDateFact().Value(Context, new DateOnly(year, month, day)));
    }
}
