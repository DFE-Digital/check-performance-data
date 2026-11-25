using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #938: the admin pages warn when the window's sample schools differ from those of an exercise's
// live release, because the change has an effect only after the exercise validates again.
public sealed class SampleSchoolsChangedTests
{
    private static CheckingWindowDto Window(string[] windowList, string[]? liveList)
    {
        var releaseId = Guid.NewGuid();
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1),
            CurrentReleaseId = liveList is null ? null : releaseId,
            Releases = liveList is null ? [] : [new CheckingExerciseReleaseDto { Id = releaseId, SampleLaestabs = liveList }]
        };
        return new CheckingWindowDto
        {
            Title = "KS2", KeyStage = KeyStages.KS2, CheckingWindowType = CheckingWindowType.KS2,
            StartDate = exercise.StartDate, EndDate = exercise.EndDate,
            SampleLaestabs = windowList, Exercises = [exercise]
        };
    }

    [Theory]
    [InlineData(new[] { "9334290" }, new string[0], true)]
    [InlineData(new string[0], new[] { "9334290" }, true)]
    [InlineData(new[] { "9334290" }, new[] { "9334291" }, true)]
    [InlineData(new[] { "9334290", "9334291" }, new[] { "9334291", "9334290" }, false)]
    [InlineData(new string[0], new string[0], false)]
    public void Compares_the_window_list_with_the_live_release(string[] windowList, string[] liveList, bool changed)
    {
        var window = Window(windowList, liveList);

        Assert.Equal(changed, window.SampleSchoolsChangedSinceLiveRelease(window.Exercises[0]));
    }

    [Fact]
    public void An_exercise_with_no_live_release_has_nothing_to_warn_about()
    {
        // Its first validation applies the list.
        var window = Window(["9334290"], liveList: null);

        Assert.False(window.SampleSchoolsChangedSinceLiveRelease(window.Exercises[0]));
    }
}
