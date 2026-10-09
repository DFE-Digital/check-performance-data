using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Which checking exercises a window type runs by default (#319). The create wizard gives a new
/// window these and asks nothing. The admin may then tick or untick any exercise on the window's
/// exercises page, so this is a starting point rather than a rule — which is how KS4 Autumn can be
/// given a results enquiry without a code change, the gap docs/16-19-window-model.md opens with.
/// </summary>
/// <remarks>
/// A new <see cref="CheckingExerciseType"/> appears on the exercises page from the enum alone, with
/// no row here — the page lists every member. This table only decides what a new window starts
/// with, so an unmapped window type falling back to pupil data checking is a sensible default rather than a silent
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
    /// How a new exercise shows its data to schools. KS4 June and KS2 pupil data each have one
    /// supplier file in which each pupil carries their own inclusion code, so they start with an
    /// included tab and a non-included tab. Everything else starts as a table. The admin can change
    /// it on the exercise's edit page.
    /// </summary>
    public static ExerciseLayout DefaultLayout(CheckingWindowType windowType, CheckingExerciseType? exercise) =>
        windowType is CheckingWindowType.KS4June or CheckingWindowType.KS2 && exercise == CheckingExerciseType.PupilData
            ? ExerciseLayout.InclusionTabs
            : ExerciseLayout.Table;

    /// <summary>
    /// The tab order a new exercise of this kind starts with: enum order, in steps of 100 so an
    /// admin can put a data share between two kinds without renumbering them.
    /// </summary>
    public static int DefaultTabOrder(CheckingExerciseType exercise) => ((int)exercise + 1) * 100;
}
