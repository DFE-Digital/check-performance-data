using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// What the check-your-pupil-data page may offer right now. The page offers whatever is open, for
/// any number of exercises, so the options follow the exercise dates rather than the window type
/// (#317).
/// </summary>
public interface INextStepsService
{
    /// <summary>Next-step options for the exercises open right now, in display order.</summary>
    IReadOnlyList<NextSteps> GetAvailableSteps(IReadOnlyList<CheckingExerciseDto> exercises);
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

    public IReadOnlyList<NextSteps> GetAvailableSteps(IReadOnlyList<CheckingExerciseDto> exercises) =>
        checkingExercises.OpenCheckingExercises(exercises)
            // An exercise with no mapping contributes nothing. Fail closed rather than throw: it
            // must not offer a journey with nothing behind it, but nor should one unmapped row take
            // the whole page down. NextStepsServiceTests pins that every type that exists is mapped.
            .SelectMany(e => StepsByExercise.TryGetValue(e, out var steps) ? steps : [])
            .ToList();
}
