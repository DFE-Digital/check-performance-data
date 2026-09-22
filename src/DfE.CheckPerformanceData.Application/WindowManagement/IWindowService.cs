using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowService
{
    Task<PageResult?> GetAllDataAsync(CancellationToken cancellationToken);
    /// Null when no window has that id — every caller is an admin route keyed on a URL segment.
    Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
    Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken);

    /// <summary>Adds a display-only exercise (#466). Rules and reasons are the service's.</summary>
    Task<ExerciseChangeResult> AddExerciseAsync(Guid windowId, ExerciseDefinition definition, CancellationToken cancellationToken);
    /// <summary>Renames, reorders or re-dates one exercise. Its kind and slots are untouched.</summary>
    Task<ExerciseChangeResult> UpdateExerciseAsync(Guid windowId, Guid exerciseId, ExerciseDefinition definition, CancellationToken cancellationToken);
    /// <summary>Removes one exercise and its dataset rows. Refused when it holds change requests.</summary>
    Task<ExerciseChangeResult> RemoveExerciseAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>Sets which exercises the window runs, by name (#466): keeps every existing exercise
    /// named, adds every template named that the window lacks (on the window's own dates), removes
    /// the rest. Refused when a removed exercise holds change requests, or when nothing would
    /// remain.</summary>
    Task<ExerciseChangeResult> SetExercisesAsync(Guid windowId, IReadOnlyCollection<string> selectedNames, CancellationToken cancellationToken);
}

public class PageResult
{
    public required List<CheckingWindowDto> Windows { get; set; }
}

/// <summary>What an admin types for an exercise (#466). Kind is never here — it is immutable.</summary>
public sealed record ExerciseDefinition(string Name, string TabName, int SortOrder, DateTime StartDate, DateTime EndDate)
{
    // Single source of truth for the column widths: Persistence's CheckingExerciseConfiguration
    // reads these same constants for HasMaxLength, so a length that Validate() lets through is
    // guaranteed to fit the column.
    public const int MaxNameLength = 200;
    public const int MaxTabNameLength = 100;
}

/// <summary>A refused change carries the reason the page shows. Never thrown.</summary>
public sealed record ExerciseChangeResult(bool Succeeded, string? Reason)
{
    public static ExerciseChangeResult Ok() => new(true, null);
    public static ExerciseChangeResult Refused(string reason) => new(false, reason);
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

    /// <summary>AB#298317: the next chance to review data, shown to schools as month + year. Null = not set.</summary>
    public DateTime? NextOpportunity { get; set; }

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

    /// <summary>The exercise of this kind, or null when the window does not run it. A display-only
    /// exercise has no kind and is never returned here — reach it by id.</summary>
    public CheckingExerciseDto? FindExercise(CheckingExerciseType exercise) =>
        Exercises.SingleOrDefault(e => e.ExerciseType == exercise);

    /// <summary>The exercise with this id, or null. The admin routes key on this (#466).</summary>
    public CheckingExerciseDto? FindExercise(Guid id) =>
        Exercises.SingleOrDefault(e => e.Id == id);

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

    /// <summary>
    /// What the exercise does: pupil data checking, results enquiry — or <c>null</c> for a
    /// display-only data share the admin defined (#466). Journeys, next steps and close all look
    /// an exercise up by kind, so a null-kind exercise offers none of them.
    /// </summary>
    public required CheckingExerciseType? ExerciseType { get; init; }

    /// <summary>Heading for admins and, from slice 3, schools. Defaulted from
    /// <see cref="CheckingExerciseNames"/> for a kind exercise; typed by the admin otherwise.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short tab label. Stored now, rendered in slice 3.</summary>
    public string TabName { get; set; } = string.Empty;

    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public int SortOrder { get; set; }

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

    /// <summary>
    /// Every required dataset has both its files, and at least one file pair is present — so the
    /// exercise can be validated. Optional slots may be empty (#324): the results feed's late,
    /// revised and retention files arrive weeks apart and one of them may never arrive at all, so
    /// waiting for every slot would mean never validating.
    /// </summary>
    public bool HasRequiredFiles =>
        Datasets.Any(d => d.IsComplete) && Datasets.Where(d => d.Required).All(d => d.IsComplete);

    /// <summary>
    /// Validate can run: the files are present AND the exercise has a kind. A display-only exercise
    /// has no blob prefix until slice 2, so <see cref="CheckingExerciseBlobPaths"/> must never be
    /// reached for one — this is the gate that stops it.
    /// </summary>
    public bool CanValidate => HasRequiredFiles && ExerciseType is not null;

    /// <summary>
    /// The datasets a run actually reads, in sort order — the complete ones. An empty optional slot
    /// is a file that has not arrived, not a file to fail on, and a run rewrites the exercise's
    /// whole output, so the same exercise is simply re-run when the next file lands.
    /// </summary>
    public IReadOnlyList<CheckingWindowDatasetDto> DatasetsToIngest =>
        [.. Datasets.Where(d => d.IsComplete).OrderBy(d => d.SortOrder)];

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

    /// <summary>
    /// Stamped onto every record from this file as its SOURCE, so provenance is decided by file of
    /// origin exactly as <see cref="Included"/> decides inclusion (#324). A
    /// <see cref="ResultsEnquiry.ResultsFileTags"/> value on a results dataset; null on pupil data,
    /// where nothing is stamped.
    /// </summary>
    public string? SourceFile { get; init; }

    /// <summary>
    /// The exercise cannot be validated until this slot holds both its files. False for a slot the
    /// supplier may not deliver at all — every results file after the main one (#324).
    /// </summary>
    public bool Required { get; init; } = true;

    public int SortOrder { get; init; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}

/// <summary>
/// Which datasets a checking exercise ingests, decided by the window type it sits in and the
/// exercise itself. Pupil data checking takes the supplier's pupil files — two for Post16, because
/// the non-included file has no P_INCL column — and a results enquiry takes one file per source in
/// the six-file results feed, each named by its <see cref="ResultsFileTags"/> tag (#324).
/// </summary>
/// <remarks>
/// An exercise type with no row here gets no dataset slots rather than a throw: an exercise is
/// allowed to hold no datasets, so an unmapped type is an exercise nothing ingests yet — visible on
/// the summary page as "This exercise has no ingress files to load" — not a silent misfile. That is
/// the opposite of <see cref="CheckingExerciseBlobPaths"/>, where a missing row would let one
/// exercise write over another's blobs and so must fail loudly.
/// </remarks>
public static class WindowDatasets
{
    public const string Included = "included";
    public const string NonIncluded = "nonincluded";
    public const string Pupils = "pupils";

    public static IReadOnlyList<CheckingWindowDatasetDto> DefaultsFor(
        CheckingWindowType type, CheckingExerciseType exercise) =>
        exercise switch
        {
            CheckingExerciseType.PupilData => PupilDataDefaults(type),
            CheckingExerciseType.ResultsEnquiry => ResultsEnquiryDefaults(type),
            _ => []
        };

    private static IReadOnlyList<CheckingWindowDatasetDto> PupilDataDefaults(CheckingWindowType type) =>
        type == CheckingWindowType.Post16
            ?
            [
                new CheckingWindowDatasetDto { Name = Included, Included = true, SortOrder = 0 },
                new CheckingWindowDatasetDto { Name = NonIncluded, Included = false, SortOrder = 1 }
            ]
            : [ new CheckingWindowDatasetDto { Name = Pupils, Included = null, SortOrder = 0 } ];

    // One slot per source file. The slot is named by the tag it stamps, so the admin uploading the
    // files sees the supplier's own file names and a dataset can never be given the wrong tag.
    // KS2 has no results feed, so a results enquiry on a KS2 window gets no slots at all.
    private static IReadOnlyList<CheckingWindowDatasetDto> ResultsEnquiryDefaults(CheckingWindowType type) =>
        type switch
        {
            CheckingWindowType.Post16 => Slots(
                ResultsFileTags.Post16Main,
                ResultsFileTags.Post16LateResults1,
                ResultsFileTags.Post16LateResults2,
                ResultsFileTags.Post16Revised,
                ResultsFileTags.Post16Retention),
            CheckingWindowType.KS4June or CheckingWindowType.KS4Autumn => Slots(
                ResultsFileTags.Ks4Main,
                ResultsFileTags.Ks4LateResults1,
                ResultsFileTags.Ks4LateResults2,
                ResultsFileTags.Ks4Revised),
            _ => []
        };

    // Only the main file is required. The late, revised and retention files land weeks apart and
    // one may never land — an exercise that could not be validated until all of them had arrived
    // would leave a school with no results at all in the meantime.
    private static IReadOnlyList<CheckingWindowDatasetDto> Slots(params string[] tags) =>
        [.. tags.Select((tag, index) => new CheckingWindowDatasetDto
        {
            Name = tag,
            SourceFile = tag,
            // Inclusion is a pupil-data concept: a result row is not included or non-included.
            Included = null,
            Required = index == 0,
            SortOrder = index
        })];
}

/// <summary>An admin's own exercise takes one required slot named <c>data</c> (#466).</summary>
public static class CustomExerciseDatasets
{
    public const string Data = "data";

    public static CheckingWindowDatasetDto Slot() =>
        new() { Name = Data, Included = null, Required = true, SortOrder = 0 };
}
