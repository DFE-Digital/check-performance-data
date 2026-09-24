using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class WindowViewModel(IReadOnlyList<WindowListItem> windows)
{
    public IReadOnlyList<WindowListItem> Windows { get; } = windows;

    /// <summary>The user holds the new-window section, so the page offers the New window button.</summary>
    public bool CanCreateWindow { get; init; }
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
        get => $"{BaseEditUrl}/window-type";
    }

    /// <summary>
    /// One section per checking exercise (#319). Each carries its own dates, its own ingress and
    /// schema files, and its own validation state — a window is no longer validated as a whole.
    /// </summary>
    public IReadOnlyList<ExerciseSummarySection> Exercises { get; set; } = [];

    public string ExercisesLink => $"{BaseEditUrl}/exercises";

    /// <summary>Every section's Add checking exercise link (#466 slice 3) goes here, keyed by
    /// window id — <see cref="Controllers.WindowAdmin.CreateCheckingExerciseController.New"/>.</summary>
    public string AddExerciseLink => $"{BaseEditUrl}/exercises/new";

    public string? OutputPath { get; set; }
    public bool IsPublished { get; set; } = false;
    public Guid? PublishedId { get; set; }
}

/// <summary>One checking exercise on the window summary page (#466 slice 3). A display-only data
/// share (null <see cref="ExerciseType"/>) appears here on the same footing as a kind-bearing
/// exercise — every link is addressed by <see cref="Id"/>, never by kind, because a data share has
/// no kind to route by.</summary>
public sealed class ExerciseSummarySection
{
    public required Guid WindowId { get; init; }
    public required Guid Id { get; init; }
    public required CheckingExerciseType? ExerciseType { get; init; }

    /// <summary>The admin's own name for this exercise, falling back to the generic kind label
    /// ("Data share") only when it has none — see <c>ExerciseLabels.For(CheckingExerciseType?)</c>
    /// in <c>SummaryController</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The exercise's kind, or "Data share" when it has none. Distinct from <see cref="Name"/>,
    /// which is the admin's own label for this exercise.</summary>
    public required string KindLabel { get; init; }

    public required string TabName { get; init; }
    public bool IsEnabled { get; init; }
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

    /// <summary>Everything about this exercise — name, tab, kind, dates, visibility, data files —
    /// is edited on the one page (<c>EditCheckingExerciseController.Edit</c>), so the summary has a
    /// single Edit link per section rather than a Change link per field.</summary>
    public string EditLink => $"/admin/windows/{WindowId}/exercises/{Id}/edit";

    /// <summary>Exercise-id addressed (#466 slice 2/3): the only route a display-only exercise can
    /// use to validate, since it has no kind to name in the kind-addressed route.</summary>
    public string ValidateLink => $"/admin/windows/{WindowId}/exercises/{Id}/validate";

    // No IsValidatable-style gate beside this one: closing works regardless of the exercise's dates
    // and regardless of whether its files ever validated. It is an admin decision, not a
    // consequence of the clock — see ICloseExerciseService.
    //
    // Kind-addressed still: closing replays ChangeRequests, which are only ever routed to a kind
    // via WhatToChangeCheckingExerciseMap, so a display-only exercise (no kind) can hold none to
    // close. The view renders this action only when ExerciseType is not null.
    public string CloseLink => $"/admin/windows/{WindowId}/{ExerciseType}/close";

    // Every REQUIRED dataset must have both files — a Post16 pupil-data exercise is not validatable
    // until both the included and non-included CSV/schema pairs are chosen, because they ingest in
    // one run. An exercise with no complete dataset at all has nothing to validate. Optional slots
    // (#324) may be empty: a results file that has not been delivered yet must not hold up the ones
    // that have.
    private bool HasRequiredFiles =>
        Datasets.Any(d => d.IsComplete) && Datasets.Where(d => d.Required).All(d => d.IsComplete);

    private bool HasValidDates
    {
        get
        {
            var today = DateTime.UtcNow.Date;

            return EndDate.Date >= today && EndDate.Date >= StartDate.Date;
        }
    }

    public bool IsValidatable => HasValidDates && HasRequiredFiles;
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

    // Exercise-id addressed, same reason as ExerciseSummarySection.ValidateLink: a display-only
    // exercise has no kind, so the kind-addressed route would not exist for it.
    public string IngressFileLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/ingress-file/{Name}";
    public string SchemaFileLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/schema-file/{Name}";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}
