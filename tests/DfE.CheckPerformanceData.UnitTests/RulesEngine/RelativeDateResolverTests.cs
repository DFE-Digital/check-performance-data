using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// #800: the resolver turns a window-relative date form into a concrete date
/// from the window start date. It is a pure function: the clock is never
/// consulted, so the same window start date always gives the same date.
/// Synthetic 2025/2026/2027 windows pin the year math so a regression is
/// caught.
/// </summary>
public sealed class RelativeDateResolverTests
{
    // --- dayMonthOfWindowYear ---

    [Fact]
    public void DayMonthOfWindowYear_ResolvesDayMonthInTheWindowYear()
    {
        var spec = new RelativeDateSpec.DayMonthOfWindowYear(4, 1);

        var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(2027, 9, 3), out var date);

        Assert.True(ok);
        Assert.Equal(new DateOnly(2027, 4, 1), date);
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void DayMonthOfWindowYear_UsesTheWindowStartYear(int windowYear)
    {
        var spec = new RelativeDateSpec.DayMonthOfWindowYear(9, 1);

        var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(windowYear, 9, 3), out var date);

        Assert.True(ok);
        Assert.Equal(new DateOnly(windowYear, 9, 1), date);
    }

    [Fact]
    public void DayMonthOfWindowYear_Feb29_ResolvesInALeapWindowYear()
    {
        var spec = new RelativeDateSpec.DayMonthOfWindowYear(2, 29);

        var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(2024, 9, 2), out var date);

        Assert.True(ok);
        Assert.Equal(new DateOnly(2024, 2, 29), date);
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    public void DayMonthOfWindowYear_Feb29_IsUnresolvableInANonLeapWindowYear(int windowYear)
    {
        var spec = new RelativeDateSpec.DayMonthOfWindowYear(2, 29);

        Assert.False(RelativeDateResolver.TryResolve(spec, new DateOnly(windowYear, 9, 2), out _));
    }

    // --- firstSeptemberMinusYears ---

    [Theory]
    [InlineData(2025, 2022)]
    [InlineData(2026, 2023)]
    [InlineData(2027, 2024)]
    public void FirstSeptemberMinusYears_ResolvesTheMinusYearsSeptember(int windowYear, int expectedYear)
    {
        var spec = new RelativeDateSpec.FirstSeptemberMinusYears(3);

        var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(windowYear, 9, 3), out var date);

        Assert.True(ok);
        Assert.Equal(new DateOnly(expectedYear, 9, 1), date);
    }

    [Fact]
    public void FirstSeptemberMinusYears_ZeroYears_IsTheWindowYearSeptember()
    {
        var spec = new RelativeDateSpec.FirstSeptemberMinusYears(0);

        var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(2027, 9, 3), out var date);

        Assert.True(ok);
        Assert.Equal(new DateOnly(2027, 9, 1), date);
    }

    // --- secondMondayOfMay ---

    [Theory]
    [InlineData(2025, 12)]
    [InlineData(2026, 11)]
    [InlineData(2027, 10)]
    public void SecondMondayOfMay_MatchesTheKnownAnchors(int windowYear, int expectedDay)
    {
        var spec = new RelativeDateSpec.SecondMondayOfMay();

        var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(windowYear, 9, 3), out var date);

        Assert.True(ok);
        Assert.Equal(new DateOnly(windowYear, 5, expectedDay), date);
    }

    // --- no window start date ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NullWindowStartDate_IsUnresolvableForEveryForm(int form)
    {
        RelativeDateSpec spec = form switch
        {
            0 => new RelativeDateSpec.DayMonthOfWindowYear(4, 1),
            1 => new RelativeDateSpec.FirstSeptemberMinusYears(3),
            2 => new RelativeDateSpec.SecondMondayOfMay(),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        Assert.False(RelativeDateResolver.TryResolve(spec, null, out _));
    }
}
