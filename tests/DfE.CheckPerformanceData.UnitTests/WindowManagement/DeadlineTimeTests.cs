using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#301022: a deadline sentence used to print the hour only. That was invisible while every end
// date sat on the hour, and wrong the moment an exercise was closed early at 12:35 ("passed at
// 12pm"). One formatter now decides the clock time for every deadline sentence.
public sealed class DeadlineTimeTests
{
    [Theory]
    [InlineData(17, 0, 0, "5pm")]           // the default end: unchanged
    [InlineData(0, 0, 0, "12am")]
    [InlineData(12, 0, 0, "12pm")]
    [InlineData(12, 35, 0, "12:35pm")]      // an early close, or an end typed with minutes
    [InlineData(12, 34, 59, "12:34pm")]     // an early close is stored one second before the press
    [InlineData(9, 5, 0, "9:05am")]
    public void The_minutes_are_shown_only_when_there_are_some(int hour, int minute, int second, string expected)
        => Assert.Equal(expected, DeadlineTime.Format(new DateTime(2026, 10, 1, hour, minute, second)));

    [Theory]
    [InlineData(17, 0, "htt")]
    [InlineData(17, 30, "h:mmtt")]
    public void The_pattern_is_offered_for_sentences_that_format_the_date_in_the_same_call(int hour, int minute, string expected)
        => Assert.Equal(expected, DeadlineTime.Pattern(new DateTime(2026, 10, 1, hour, minute, 0)));
}
