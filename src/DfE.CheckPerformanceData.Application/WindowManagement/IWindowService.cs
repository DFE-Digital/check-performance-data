using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowService
{
    Task<PageResult?> GetAllDataAsync(CancellationToken cancellationToken);
    Task<CheckingWindowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
    Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
}

public class PageResult
{
    public required List<CheckingWindowDto> Windows { get; set; }
}

public sealed class CheckingWindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; set; }
    public required DateTime EndDate { get; set; }
    public required KeyStages KeyStage { get; set; }
    public required CheckingWindowType CheckingWindowType { get; set; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; set; }
    public string IngressFile { get; set; } = string.Empty;
    public string IngressFileChecksum { get; set; } = string.Empty;
    public string SchemaFile { get; set; } = string.Empty;
    public string SchemaFileChecksum { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string TurnaroundCommitment { get; set; } = string.Empty;

    // #319: Validated / ValidatedAt are gone from here. A window is not validated as a whole — ask
    // a CheckingExerciseDto, or fold the answer across Exercises.

    /// <summary>
    /// The window's checking exercises, in sort order. A dataset belongs to the exercise that
    /// consumes it, so this is the only route to the window's ingress files. The legacy scalar
    /// IngressFile/SchemaFile properties above are kept for one release for rollback safety and
    /// mirror the first dataset.
    /// </summary>
    public List<CheckingExerciseDto> Exercises { get; set; } = [];

    // #319: AllDatasets is gone. It flattened every exercise's datasets into one list, which was
    // only ever right while a single exercise held them all — the admin wizard, the summary page
    // and the validate run are all per-exercise now, and each asks the exercise it means.

    /// <summary>The exercise of this type, or null when the window does not run it.</summary>
    public CheckingExerciseDto? FindExercise(CheckingExerciseType exercise) =>
        Exercises.SingleOrDefault(e => e.ExerciseType == exercise);

    /// <summary>
    /// The outer pair derived from the exercises: earliest start, latest end. The wizard never asks
    /// an admin for the window's own dates, so the two can never disagree. A window with no
    /// exercises keeps whatever it has — there is nothing to derive from.
    /// </summary>
    public void DeriveDatesFromExercises()
    {
        if (Exercises.Count == 0) return;

        StartDate = Exercises.Min(e => e.StartDate);
        EndDate = Exercises.Max(e => e.EndDate);
    }
}

public sealed class CheckingExerciseDto
{
    public Guid Id { get; init; }
    public required CheckingExerciseType ExerciseType { get; init; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public int SortOrder { get; init; }

    /// <summary>
    /// The CSV + schema pairs this exercise ingests, in sort order. Any number, including none.
    /// </summary>
    public List<CheckingWindowDatasetDto> Datasets { get; set; } = [];

    /// <summary>When this exercise last validated cleanly. Null = never (#319).</summary>
    public DateTime? ValidatedAt { get; set; }

    /// <summary>
    /// The dataset checksums the stamp was taken over. When these no longer match the exercise's
    /// current datasets, the stamp describes files that have since been replaced.
    /// </summary>
    public string ValidatedIngressChecksum { get; set; } = string.Empty;
    public string ValidatedSchemaChecksum { get; set; } = string.Empty;

    /// <summary>Every dataset has both its files, so the exercise can be validated.</summary>
    public bool HasRequiredFiles => Datasets.Count > 0 && Datasets.All(d => d.IsComplete);

    /// <summary>
    /// Validated, and against the files it currently holds. A stamp taken before an ingress file
    /// was swapped is stale, and saying so is the only reason the checksums are stored.
    /// </summary>
    public bool IsValidated =>
        ValidatedAt is not null
        && ValidatedIngressChecksum == CurrentIngressChecksum
        && ValidatedSchemaChecksum == CurrentSchemaChecksum;

    /// <summary>The dataset ingress checksums as they stand, in dataset order.</summary>
    public string CurrentIngressChecksum => Combine(Datasets.OrderBy(d => d.SortOrder).Select(d => d.IngressFileChecksum));

    /// <summary>The dataset schema checksums as they stand, in dataset order.</summary>
    public string CurrentSchemaChecksum => Combine(Datasets.OrderBy(d => d.SortOrder).Select(d => d.SchemaFileChecksum));

    // Hashed rather than joined: each part is 64 hex characters, and an exercise with six datasets
    // (the results-enquiry shape) would overflow the 256-character column on a plain join.
    private static string Combine(IEnumerable<string> checksums) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join("|", checksums))));
}

public sealed class CheckingWindowDatasetDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string IngressFile { get; set; } = string.Empty;
    public string IngressFileChecksum { get; set; } = string.Empty;
    public string SchemaFile { get; set; } = string.Empty;
    public string SchemaFileChecksum { get; set; } = string.Empty;

    /// <summary>Stamped onto every record from this file. Null = the record carries its own
    /// inclusion signal (KS4's P_INCL).</summary>
    public bool? Included { get; init; }

    public int SortOrder { get; init; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}

/// <summary>
/// Which datasets a checking window ingests, decided by its type. Post16 is the only type where
/// the supplier delivers pupils as two files.
/// </summary>
public static class WindowDatasets
{
    public const string Included = "included";
    public const string NonIncluded = "nonincluded";
    public const string Pupils = "pupils";

    public static IReadOnlyList<CheckingWindowDatasetDto> DefaultsFor(CheckingWindowType type) =>
        type == CheckingWindowType.Post16
            ?
            [
                new CheckingWindowDatasetDto { Name = Included, Included = true, SortOrder = 0 },
                new CheckingWindowDatasetDto { Name = NonIncluded, Included = false, SortOrder = 1 }
            ]
            : [ new CheckingWindowDatasetDto { Name = Pupils, Included = null, SortOrder = 0 } ];
}

