using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class WindowViewModel(IReadOnlyList<WindowListItem> windows)
{
    public IReadOnlyList<WindowListItem> Windows { get; } = windows;
}

public class WindowListItem
{
    public IReadOnlyList<CheckingExerciseListItem> Exercises { get; init; } = [];
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsOpen { get; init; } = false;
    public bool IsPublished { get; init; } = false;
}

public sealed class CheckingExerciseListItem
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public IReadOnlyList<string> MissingJourneys { get; init; } = [];
    public string TagColour => Status switch
    {
        "Missing journeys" => "red",
        "Closed" => "grey",
        _ => "green"
    };
}

public class WindowEditItem : AdminPage
{ 
    private string BaseEditUrl => $"/admin/windows/{WindowId}";
    public required string Title { get; set; }
    public string TitleLink
    {
        get => $"{BaseEditUrl}/title";
    }
    public string TurnaroundCommitment { get; set; } = string.Empty;
    public string TurnaroundCommitmentLink
    {
        get => $"{BaseEditUrl}/turnaround-commitment";
    }

    /// <summary>AB#298317: already formatted as month + year (<c>NextOpportunityText</c>); null = not set.</summary>
    public string? NextOpportunity { get; set; }
    public string NextOpportunityLink
    {
        get => $"{BaseEditUrl}/next-opportunity";
    }

    public bool IsOpen { get; set; } = false;
    // #319: derived from the exercises as their union, so there is no Change link — the outer pair
    // is never typed. To move a window's dates, move an exercise's.
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }

    public required KeyStages KeyStage { get; set; }
    public required CheckingWindowType CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink {
        get => $"{BaseEditUrl}/checking-window-type";
    }

    /// <summary>
    /// One section per checking exercise (#319). Each carries its own dates, its own ingress and
    /// schema files, and its own validation state — a window is no longer validated as a whole.
    /// </summary>
    public IReadOnlyList<ExerciseSummarySection> Exercises { get; set; } = [];

    public string ExercisesLink => $"{BaseEditUrl}/exercises";

    /// <summary>Served by AddExerciseController (#466).</summary>
    public string AddExerciseLink => $"{BaseEditUrl}/exercises/add";

    public string? OutputPath { get; set; }
    public bool IsPublished { get; set; } = false;
    public Guid? PublishedId { get; set; }
}

/// <summary>One checking exercise on the window summary page (#466).</summary>
public sealed class ExerciseSummarySection
{
    public const string NoStorageReason = "Validate is not available for a data share until its storage is ready";

    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }

    /// <summary>Null = display-only data share, admin-defined and with no blob prefix yet.</summary>
    public CheckingExerciseType? ExerciseType { get; init; }
    public required string Label { get; init; }
    public required string TabName { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }

    /// <summary>One row pair per ingress dataset. A Post16 pupil-data exercise has two (included +
    /// non-included); every other type has one. An exercise with no ingress step yet has none.</summary>
    public IReadOnlyList<DatasetSummaryRow> Datasets { get; init; } = [];

    /// <summary>Validated, against the files it currently holds.</summary>
    public bool IsValidated { get; init; }

    public DateTime? ValidatedAt { get; init; }

    /// <summary>Validated once, but not against the files it holds now — a stale stamp.</summary>
    public bool IsStale { get; init; }

    private string BaseUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}";

    /// <summary>Served by EditExerciseController (#466).</summary>
    public string EditLink => $"{BaseUrl}/edit";

    /// <summary>Served by RemoveExerciseController (#466).</summary>
    public string RemoveLink => $"{BaseUrl}/remove";

    public string ValidateLink => $"{BaseUrl}/validate";

    // No IsValidatable-style gate beside this one: closing works regardless of the exercise's dates
    // and regardless of whether its files ever validated. It is an admin decision, not a
    // consequence of the clock — see ICloseExerciseService.
    public string CloseLink => $"{BaseUrl}/close";

    /// <summary>Only a kind exercise has journeys to sweep — a display-only data share has nothing
    /// for ICloseExerciseService to close.</summary>
    public bool CanClose => ExerciseType is not null;

    /// <summary>
    /// Projected straight from <see cref="CheckingExerciseDto.CanValidate"/> (#466 review fix) —
    /// the page must agree with <c>ValidateWindowController</c>'s own guard by construction, not by
    /// a second, drifting copy of "files present". There is deliberately no date condition here:
    /// nothing compares an exercise's dates to the clock outside <c>ICheckingExerciseService</c>,
    /// and Validate has never depended on the clock — only Close does not, and Validate is the
    /// same kind of admin action.
    /// </summary>
    public required bool IsValidatable { get; init; }

    /// <summary>The message shown in place of the Validate button when it is not available.
    /// Null-kind exercise: no data store yet. Otherwise: the required files are not all present.</summary>
    public string? ValidateDisabledReason => ExerciseType is null ? NoStorageReason : null;
}

public sealed class DatasetSummaryRow
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? IngressFile { get; init; }
    public string? SchemaFile { get; init; }

    /// <summary>The exercise cannot be validated until this slot holds both files (#324).</summary>
    public bool Required { get; init; } = true;

    public string IngressFileLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/ingress-file/{Name}";
    public string SchemaFileLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/schema-file/{Name}";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}
