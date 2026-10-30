using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// What the check-your-pupil-data page and the submission confirmation pages may offer right now.
/// Each page offers whatever is open, for any number of exercises, so the options follow the
/// exercise dates rather than the window type (#317). The confirmation pages exclude
/// <see cref="NextSteps.Confirm"/> and <see cref="NextSteps.SignOut"/> (#713).
/// </summary>
public interface INextStepsService
{
    /// <summary>
    /// Next-step options for the exercises open right now for this school, in display order
    /// (AB#307773); a null or empty LAESTAB is the exercises' own dates.
    /// </summary>
    IReadOnlyList<NextSteps> GetAvailableSteps(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab);

    /// <summary>
    /// What a submission confirmation page may offer right now for this school, in display order:
    /// the same open exercises as <see cref="GetAvailableSteps"/>, minus <see cref="NextSteps.Confirm"/>
    /// (a confirmation page is not the data-confirmation journey) and <see cref="NextSteps.SignOut"/>
    /// (the service never offers it). An empty list is a valid answer: the page then offers only
    /// its fixed links (#713).
    /// </summary>
    IReadOnlyList<NextSteps> GetConfirmationPageSteps(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab);
}

/// <inheritdoc />
/// <remarks>
/// The mapping is domain knowledge, so it lives here rather than in the controller. Adding a future
/// exercise type must mean adding a row to <see cref="StepsByExercise"/>, never editing branching
/// logic — and no branch here may look at <c>CheckingWindowType</c>. Which exercises are open is
/// never decided here either: that is <see cref="ICheckingExerciseService"/>'s single job, and it
/// owns the only clock in this path.
/// </remarks>
public sealed class NextStepsService(ICheckingExerciseService checkingExercises) : INextStepsService
{
    /// <summary>
    /// One entry per exercise type. RequestChange and Confirm both belong to PupilData, so they
    /// appear and disappear together when that exercise opens and closes.
    /// </summary>
    private static readonly Dictionary<CheckingExerciseType, NextSteps[]> StepsByExercise = new()
    {
        [CheckingExerciseType.PupilData] = [NextSteps.RequestChange, NextSteps.Confirm],
        [CheckingExerciseType.ResultsEnquiry] = [NextSteps.ResultsEnquiry]
    };

    public IReadOnlyList<NextSteps> GetAvailableSteps(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab) =>
        OpenSteps(exercises, laestab);

    public IReadOnlyList<NextSteps> GetConfirmationPageSteps(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab) =>
        // A confirmation page offers the amendment and the enquiry, but not the data-confirmation
        // journey: the school has just submitted, so "confirm the data is correct" is not an action
        // it can take from here.
        OpenSteps(exercises, laestab)
            .Where(step => step != NextSteps.Confirm && step != NextSteps.SignOut)
            .ToList();

    private IReadOnlyList<NextSteps> OpenSteps(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab) =>
        checkingExercises.OpenCheckingExercises(exercises, laestab)
            // An exercise with no mapping contributes nothing. Fail closed rather than throw: it
            // must not offer a journey with nothing behind it, but nor should one unmapped row take
            // the whole page down. NextStepsServiceTests pins that every type that exists is mapped.
            .SelectMany(e => StepsByExercise.TryGetValue(e, out var steps) ? steps : [])
            .ToList();
}
