using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

// AB#315496: the amendment requests screen says "amendment requests" (pupil/student) and
// "result enquiries", not "data checking requests". These pin the exact sentence for every
// open/closed, learner and exercise combination the page renders.
public sealed class ExerciseDeadlineViewModelTests
{
    // 26 June 2026 17:00 formats as "5pm on Friday 26 June 2026" (see Index_FormatsDeadlineText).
    private static readonly DateTime EndDate = new(2026, 6, 26, 17, 0, 0);

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, CheckingWindowType.KS4June, true,
        "Submit your pupil amendment requests by 5pm on Friday 26 June 2026")]
    [InlineData(CheckingExerciseType.PupilData, CheckingWindowType.KS4June, false,
        "The deadline for pupil amendment requests passed at 5pm on Friday 26 June 2026")]
    [InlineData(CheckingExerciseType.PupilData, CheckingWindowType.Post16, true,
        "Submit your student amendment requests by 5pm on Friday 26 June 2026")]
    [InlineData(CheckingExerciseType.PupilData, CheckingWindowType.Post16, false,
        "The deadline for student amendment requests passed at 5pm on Friday 26 June 2026")]
    [InlineData(CheckingExerciseType.ResultsEnquiry, CheckingWindowType.KS4June, true,
        "Submit your result enquiries by 5pm on Friday 26 June 2026")]
    [InlineData(CheckingExerciseType.ResultsEnquiry, CheckingWindowType.KS4June, false,
        "The deadline for result enquiries passed at 5pm on Friday 26 June 2026")]
    public void Sentence_UsesTheAmendmentRequestsScreenWording(
        CheckingExerciseType exercise, CheckingWindowType windowType, bool isOpen, string expected)
    {
        var vm = new ExerciseDeadlineViewModel
        {
            Exercise = exercise,
            EndDate = EndDate,
            IsOpen = isOpen,
            LearnerNoun = LearnerNoun.For(windowType),
        };

        Assert.Equal(expected, vm.Sentence);
    }
}
