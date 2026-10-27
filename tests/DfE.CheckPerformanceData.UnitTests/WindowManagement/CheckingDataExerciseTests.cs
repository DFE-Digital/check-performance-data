using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.WindowManagement;

public sealed class CheckingDataExerciseTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0);

    private static CheckingDataExercise Exercise(bool enabled = true, DateTime? from = null,
        DateTime? until = null, CheckingExerciseType? type = CheckingExerciseType.PupilData,
        bool displayOnly = false, DateTime? actionEnd = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Pupil data", "Pupils", 0, type, KeyStages.KS4,
            enabled, from, until, Now.AddDays(-10), Now.AddDays(10), Now.AddDays(-5),
            actionEnd ?? Now.AddDays(5), null, true, displayOnly);

    [Fact]
    public void ADisabledExercise_IsNotVisible()
        => Assert.False(Exercise(enabled: false).IsVisible(Now));

    [Fact]
    public void AnExerciseNotYetOpen_IsNotVisible()
        => Assert.False(Exercise(from: Now.AddDays(1)).IsVisible(Now));

    [Fact]
    public void VisibleUntil_IsExclusive_SoTheTabGoesAtTheInstantItEnds()
        => Assert.False(Exercise(until: Now).IsVisible(Now));

    [Fact]
    public void ADisplayOnlyShare_IsVisibleButCannotBeActedOn()
    {
        var exercise = Exercise(displayOnly: true);

        Assert.True(exercise.IsVisible(Now));
        Assert.False(exercise.CanAct(Now));
    }

    [Fact]
    public void AnUntypedShare_CannotBeActedOn()
        => Assert.False(Exercise(type: null).CanAct(Now));

    [Fact]
    public void AnExerciseWhoseOwnDatesHavePassed_CannotBeActedOn()
        => Assert.False(Exercise(actionEnd: Now.AddDays(-1)).CanAct(Now));

    [Fact]
    public void AnOpenTypedExercise_CanBeActedOn()
        => Assert.True(Exercise().CanAct(Now));
}
