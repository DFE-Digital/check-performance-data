using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The default Name and TabName a Kind exercise is given when nothing has named it (#466). A
/// display-only exercise (null Kind) is always named by the admin, so it has no row here.
/// </summary>
/// <remarks>
/// No default case, same rule as <see cref="CheckingExerciseBlobPaths"/>: a new Kind with no
/// wording throws rather than borrowing another's. The 16-19 learner noun ("student") is a display
/// concern for slice 3; the stored default says "pupil" for every window type.
/// </remarks>
public static class CheckingExerciseNames
{
    /// <summary>Shown wherever a display-only exercise's kind is named (#466), e.g. the Edit
    /// exercise page. Not in the NameFor/TabNameFor switches — those default a kind exercise's own
    /// Name/TabName, and a display-only exercise has neither to default.</summary>
    public const string DisplayOnlyLabel = "Data share (display only)";

    public static string NameFor(CheckingExerciseType kind) => kind switch
    {
        CheckingExerciseType.PupilData => "Pupil data checking",
        CheckingExerciseType.ResultsEnquiry => "Results enquiry",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No default name for this exercise kind")
    };

    public static string TabNameFor(CheckingExerciseType kind) => kind switch
    {
        CheckingExerciseType.PupilData => "Pupils",
        CheckingExerciseType.ResultsEnquiry => "Results",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No default tab name for this exercise kind")
    };
}
