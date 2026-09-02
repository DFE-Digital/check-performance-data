using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Common;

// #318: the closed-exercise message has no default case, so a checking exercise type added without
// its own wording would throw on a live user request. These tests are what makes that safe: adding
// a member to CheckingExerciseType fails here until it is given a message.
public sealed class ClosedExerciseGuardTests
{
    [Theory]
    [MemberData(nameof(AllExercises))]
    public void Every_checking_exercise_type_has_a_message(CheckingExerciseType exercise)
    {
        var message = ClosedExerciseGuard.MessageFor(exercise, LearnerNoun.Pupil);

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Theory]
    [MemberData(nameof(AllExercises))]
    public void Every_message_says_the_deadline_has_passed_and_what_is_still_available(
        CheckingExerciseType exercise)
    {
        // Closed removes actions, never content — the message must not read as "come back later"
        // or as "your data has gone".
        var message = ClosedExerciseGuard.MessageFor(exercise, LearnerNoun.Pupil);

        Assert.Contains("deadline", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("view and download", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Each_exercise_has_its_own_wording()
    {
        var messages = Enum.GetValues<CheckingExerciseType>()
            .Select(e => ClosedExerciseGuard.MessageFor(e, LearnerNoun.Pupil))
            .ToList();

        Assert.Equal(messages.Count, messages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void An_unmapped_exercise_throws_rather_than_borrowing_another_message()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ClosedExerciseGuard.MessageFor((CheckingExerciseType)999, LearnerNoun.Pupil));

    public static TheoryData<CheckingExerciseType> AllExercises()
    {
        var data = new TheoryData<CheckingExerciseType>();
        foreach (var exercise in Enum.GetValues<CheckingExerciseType>()) data.Add(exercise);
        return data;
    }
}
