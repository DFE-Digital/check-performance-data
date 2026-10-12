using System.Globalization;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#302158: when the service hands an exercise's requests over by itself. Due from two hours
// after the exercise's end, and for one day after that. The sweep is safe to repeat, so the job
// repeats it for that day instead of remembering that it ran; after the day it stops looking.
public sealed class ExerciseHandOverScheduleTests
{
    private static readonly DateTime End = new(2026, 11, 2, 17, 0, 0);
    private static readonly ExerciseHandOverSettings Defaults = new();

    private static DateTime At(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-11-02T16:59:59", false)] // still open
    [InlineData("2026-11-02T17:00:01", false)] // closed, inside the two hours
    [InlineData("2026-11-02T18:59:59", false)] // one second short of two hours
    [InlineData("2026-11-02T19:00:00", true)]  // two hours past the end: due from this instant
    [InlineData("2026-11-03T12:00:00", true)]  // inside the catch-up day
    [InlineData("2026-11-03T18:59:59", true)]  // the catch-up day's last second
    [InlineData("2026-11-03T19:00:00", false)] // the catch-up day has passed
    public void An_exercise_is_due_from_two_hours_after_its_end_for_one_day(string now, bool due) =>
        Assert.Equal(due, ExerciseHandOverSchedule.IsDue(End, At(now), Defaults));

    [Fact]
    public void The_delay_and_the_catch_up_window_come_from_the_settings()
    {
        var settings = new ExerciseHandOverSettings
        {
            DelayAfterEnd = TimeSpan.FromMinutes(1),
            CatchUpWindow = TimeSpan.FromMinutes(10)
        };

        Assert.False(ExerciseHandOverSchedule.IsDue(End, At("2026-11-02T17:00:59"), settings));
        Assert.True(ExerciseHandOverSchedule.IsDue(End, At("2026-11-02T17:01:00"), settings));
        Assert.True(ExerciseHandOverSchedule.IsDue(End, At("2026-11-02T17:10:59"), settings));
        Assert.False(ExerciseHandOverSchedule.IsDue(End, At("2026-11-02T17:11:00"), settings));
    }

    [Fact]
    public void The_defaults_are_on_two_hours_one_day_and_five_minutes()
    {
        Assert.Equal("ExerciseHandOver", ExerciseHandOverSettings.SectionName);
        Assert.True(Defaults.Enabled);
        Assert.Equal(TimeSpan.FromHours(2), Defaults.DelayAfterEnd);
        Assert.Equal(TimeSpan.FromHours(24), Defaults.CatchUpWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), Defaults.PollInterval);
    }

    // A mistyped setting must not make the job spin, sweep an open exercise, or never run.
    [Fact]
    public void A_setting_that_makes_no_sense_falls_back_to_its_default()
    {
        var settings = new ExerciseHandOverSettings
        {
            DelayAfterEnd = TimeSpan.FromHours(-1),
            CatchUpWindow = TimeSpan.Zero,
            PollInterval = TimeSpan.FromSeconds(-5)
        };

        Assert.Equal(TimeSpan.FromHours(2), settings.EffectiveDelayAfterEnd);
        Assert.Equal(TimeSpan.FromHours(24), settings.EffectiveCatchUpWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.EffectivePollInterval);
    }

    // Zero is a real choice for the delay ("as soon as it has ended"), unlike for the other two.
    [Fact]
    public void A_zero_delay_is_kept()
    {
        var settings = new ExerciseHandOverSettings { DelayAfterEnd = TimeSpan.Zero };

        Assert.Equal(TimeSpan.Zero, settings.EffectiveDelayAfterEnd);
        Assert.True(ExerciseHandOverSchedule.IsDue(End, End, settings));
    }
}
