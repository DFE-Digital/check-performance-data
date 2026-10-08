using System.Globalization;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #535: CheckingExerciseServiceTests fixes its clock in UTC, so it could not tell a UK clock from
// a UTC one. Every case here is an exercise on the default times — it starts at 00:00 and ends at
// 17:00 UK time — asked about at a UTC instant either side of each boundary.
public sealed class CheckingExerciseServiceUkClockTests
{
    private const CheckingExerciseType PupilData = CheckingExerciseType.PupilData;

    private static CheckingExerciseDto[] Exercise(DateTime start, DateTime end) =>
    [
        new() { Id = Guid.NewGuid(), ExerciseType = PupilData, StartDate = start, EndDate = end }
    ];

    private static CheckingExerciseService At(string utcNow) => new(new UkClockAt(utcNow));

    [Theory]
    // British Summer Time: the UK clock is an hour ahead of UTC.
    [InlineData("2026-07-15", "2026-07-14T22:59:59Z", false)] // 23:59:59 on the 14th in the UK
    [InlineData("2026-07-15", "2026-07-14T23:00:00Z", true)]  // 00:00:00 on the 15th
    [InlineData("2026-07-15", "2026-07-15T16:00:00Z", true)]  // 17:00:00, the last open instant
    [InlineData("2026-07-15", "2026-07-15T16:00:01Z", false)] // 17:00:01
    // Winter: the two clocks agree.
    [InlineData("2027-01-15", "2027-01-14T23:59:59Z", false)]
    [InlineData("2027-01-15", "2027-01-15T00:00:00Z", true)]
    [InlineData("2027-01-15", "2027-01-15T17:00:00Z", true)]
    [InlineData("2027-01-15", "2027-01-15T17:00:01Z", false)]
    // The clocks go back (25 October 2026): the day starts on BST and ends on GMT.
    [InlineData("2026-10-25", "2026-10-24T22:59:59Z", false)]
    [InlineData("2026-10-25", "2026-10-24T23:00:00Z", true)]
    [InlineData("2026-10-25", "2026-10-25T17:00:00Z", true)]
    [InlineData("2026-10-25", "2026-10-25T17:00:01Z", false)]
    // The clocks go forward (28 March 2027): the day starts on GMT and ends on BST.
    [InlineData("2027-03-28", "2027-03-27T23:59:59Z", false)]
    [InlineData("2027-03-28", "2027-03-28T00:00:00Z", true)]
    [InlineData("2027-03-28", "2027-03-28T16:00:00Z", true)]
    [InlineData("2027-03-28", "2027-03-28T16:00:01Z", false)]
    public void An_exercise_from_midnight_to_5pm_opens_and_closes_on_the_UK_clock(
        string day, string utcNow, bool open)
    {
        DateTime date = DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        CheckingExerciseDto[] exercises = Exercise(date, date.AddHours(17));

        Assert.Equal(open, At(utcNow).IsOpen(exercises, PupilData));
    }

    [Fact]
    public void An_exercise_has_closed_one_second_after_5pm_UK_time_in_summer()
    {
        CheckingExerciseDto[] exercises = Exercise(new DateTime(2026, 7, 1), new DateTime(2026, 7, 15, 17, 0, 0));

        Assert.False(At("2026-07-15T16:00:00Z").HasClosed(exercises, PupilData));
        Assert.True(At("2026-07-15T16:00:01Z").HasClosed(exercises, PupilData));
    }
}
