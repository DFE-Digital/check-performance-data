using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class WindowViewModel(IReadOnlyList<WindowListItem> windows)
{
    public IReadOnlyList<WindowListItem> Windows { get; } = windows;
}

public class WindowListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsOpen { get; init; } = false;
    public bool IsPublished { get; init; } = false;
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

    public string? OutputPath { get; set; }
    public bool IsPublished { get; set; } = false;
    public Guid? PublishedId { get; set; }
}

/// <summary>One checking exercise on the window summary page.</summary>
public sealed class ExerciseSummarySection
{
    public required Guid WindowId { get; init; }
    public required CheckingExerciseType ExerciseType { get; init; }
    public required string Label { get; init; }
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

    public string DatesLink => $"/admin/windows/{WindowId}/exercises/{ExerciseType}/dates";
    public string ValidateLink => $"/admin/windows/{WindowId}/{ExerciseType}/validate";

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
    public required CheckingExerciseType Exercise { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? IngressFile { get; init; }
    public string? SchemaFile { get; init; }

    /// <summary>The exercise cannot be validated until this slot holds both files (#324).</summary>
    public bool Required { get; init; } = true;

    public string IngressFileLink => $"/admin/windows/{WindowId}/{Exercise}/ingress-file/{Name}";
    public string SchemaFileLink => $"/admin/windows/{WindowId}/{Exercise}/schema-file/{Name}";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}
