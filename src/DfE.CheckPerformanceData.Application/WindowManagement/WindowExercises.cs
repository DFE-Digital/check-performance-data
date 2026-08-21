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

    /// <summary>Display order, and the SortOrder written to each row. Enum order.</summary>
    public static int SortOrderFor(CheckingExerciseType exercise) => (int)exercise;
}
