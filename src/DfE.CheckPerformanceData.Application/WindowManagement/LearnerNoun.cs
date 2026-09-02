using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The word a window type uses for a learner. 16-19 says "student"; every other key stage says
/// "pupil". The screens are shared between key stages, so the noun is chosen at runtime and
/// carried on the view model rather than written into a view.
/// </summary>
/// <remarks>
/// Derived from the window type, never stored — there is no column for it on CheckingWindow and no
/// step in the admin wizard, so a window's noun cannot drift from its key stage.
///
/// The capitalised forms are stored rather than computed, so a table header ("Pupil name") can
/// never disagree with the sentence beneath it.
///
/// There is <b>no default case</b>, the same rule as <c>CheckingExerciseBlobPaths</c> and
/// <c>ClosedExerciseGuard.MessageFor</c>: a new <see cref="CheckingWindowType"/> must state its
/// noun rather than silently inheriting "pupil".
/// </remarks>
public sealed record LearnerNoun(
    string Singular,
    string Plural,
    string SingularCapitalised,
    string PluralCapitalised)
{
    public static LearnerNoun For(CheckingWindowType type) => type switch
    {
        CheckingWindowType.Post16 => new LearnerNoun("student", "students", "Student", "Students"),
        CheckingWindowType.KS2 or CheckingWindowType.KS4June or CheckingWindowType.KS4Autumn =>
            new LearnerNoun("pupil", "pupils", "Pupil", "Pupils"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
            "No learner noun is defined for this checking window type.")
    };

    /// <summary>The noun every key stage but 16-19 uses; the default for anything not window-scoped.</summary>
    public static LearnerNoun Pupil { get; } = new("pupil", "pupils", "Pupil", "Pupils");
}
