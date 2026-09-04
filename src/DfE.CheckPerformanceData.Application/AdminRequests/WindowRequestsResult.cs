using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AdminRequests;

/// <summary>
/// Everything the admin requests page shows for one checking window: which window it is, the
/// exercises it runs (so the filter can offer them), which one is currently selected, and the
/// matching rows.
/// </summary>
/// <remarks>
/// The page is per window now rather than service-wide, so the exercise list comes from the window
/// itself and not from <see cref="CheckingExerciseType"/>. Offering every enum member would let an
/// admin filter a KS2 window by a results enquiry it does not run and be told, correctly but
/// uselessly, that there is nothing there.
/// </remarks>
public sealed record WindowRequestsResult
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>The window's exercises in SortOrder — the filter's options.</summary>
    public required IReadOnlyList<CheckingExerciseType> Exercises { get; init; }

    /// <summary>The exercise filter in force, or null for "all exercises".</summary>
    public CheckingExerciseType? SelectedExercise { get; init; }

    public required IReadOnlyList<AdminRequestRow> Rows { get; init; }
}
