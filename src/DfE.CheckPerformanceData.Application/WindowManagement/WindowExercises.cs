using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Which checking exercises a window type runs by default (#319). The admin wizard pre-ticks these
/// and the admin may tick or untick any of them, so this is a starting point rather than a rule —
/// which is how KS4 Autumn can be given a results enquiry without a code change, the gap
/// docs/16-19-window-model.md opens with.
/// </summary>
/// <remarks>
/// A new <see cref="CheckingExerciseType"/> appears in the wizard from the enum alone, with no row
/// here — the wizard lists every member. This table only decides what starts ticked, so an unmapped
/// window type falling back to pupil data checking is a sensible default rather than a silent
/// failure, and needs no throw.
/// </remarks>
public static class WindowExercises
{
    public static IReadOnlyList<CheckingExerciseType> DefaultsFor(CheckingWindowType type) =>
        type switch
        {
            // 16-19 runs pupil data checking and results enquiry on different ranges inside one
            // window — the case the whole checking-exercise model exists for.
            CheckingWindowType.Post16 =>
                [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            _ => [CheckingExerciseType.PupilData]
        };

    /// <summary>
    /// The tab name an exercise gets when the wizard creates it. The admin can change it on the
    /// exercise's edit page. No default case: a new kind must state its own tab name.
    /// </summary>
    public static string DefaultTabName(CheckingWindowType windowType, CheckingExerciseType exercise) =>
        exercise switch
        {
            CheckingExerciseType.PupilData => LearnerNoun.For(windowType).PluralCapitalised,
            CheckingExerciseType.ResultsEnquiry => "Results",
            _ => throw new ArgumentOutOfRangeException(nameof(exercise), exercise,
                "No default tab name is defined for this checking exercise type.")
        };

    /// <summary>
    /// Whether a new exercise starts with "Show late results warning" ticked. A results enquiry
    /// opens before its late results have all arrived, so it starts ticked; the admin clears it on
    /// the exercise's edit page when they have.
    /// </summary>
    public static bool ShowsLateResultsWarningByDefault(CheckingExerciseType? exercise) =>
        exercise == CheckingExerciseType.ResultsEnquiry;

    /// <summary>
    /// The tab order a new exercise of this kind starts with: enum order, in steps of 100 so an
    /// admin can put a data share between two kinds without renumbering them.
    /// </summary>
    public static int DefaultTabOrder(CheckingExerciseType exercise) => ((int)exercise + 1) * 100;
}
