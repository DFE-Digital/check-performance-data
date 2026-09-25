using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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

    /// <summary>The exercise of this type, or null when the window does not run it. A null type
    /// finds nothing: a window may hold several typeless shares, so "the typeless one" is not a
    /// question with an answer.</summary>
    public CheckingExerciseDto? FindExercise(CheckingExerciseType? exercise) =>
        exercise is null ? null : Exercises.SingleOrDefault(e => e.ExerciseType == exercise);

    /// <summary>Schools see this window: at least one of its exercises is live at
    /// <paramref name="now"/>. A window with no live exercise is not set up yet and is hidden.</summary>
    public bool HasLiveExerciseAt(DateTime now) => Exercises.Any(e => e.IsLiveAt(now));

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

    /// <summary>
    /// The key stage derived from the window type. The wizard never asks an admin for it, so the
    /// two can never disagree, and changing the type moves the key stage with it.
    /// </summary>
    public void DeriveKeyStageFromWindowType() => KeyStage = WindowKeyStage.For(CheckingWindowType);
}

public sealed class CheckingExerciseDto
{
    public Guid Id { get; init; }
    public required CheckingExerciseType? ExerciseType { get; init; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public int SortOrder { get; init; }

    /// <summary>True for a row on the exercise-id blob layout (#466).</summary>
    public bool UsesExerciseStorage { get; init; } = true;

    /// <summary>The admin's name for this exercise.</summary>
    public string? Name { get; init; }

    /// <summary>The tab label schools see. Every exercise has one; saving an empty one is refused.</summary>
    public string TabName { get; set; } = string.Empty;
    public int TabOrder { get; init; }
    public bool IsEnabled { get; init; }
    public bool DisplayOnly { get; init; }
    public DateTime? VisibleFrom { get; init; }
    public DateTime? VisibleUntil { get; init; }
    public Guid? ReplacesCheckingExerciseId { get; init; }

    /// <summary>
    /// Schools see this exercise: it is enabled and <paramref name="now"/> is inside its visibility
    /// dates. VisibleUntil is exclusive. A window with no live exercise is not set up yet, and
    /// schools do not see it at all.
    /// </summary>
    public bool IsLiveAt(DateTime now) => IsEnabled
        && (VisibleFrom is null || VisibleFrom <= now)
        && (VisibleUntil is null || VisibleUntil > now);

    /// <summary>
    /// How the tab shows this exercise's data: a table with a dataset selector, or one record per
    /// school turned on its side. Set by the admin on the exercise, not by the schemas, so every
    /// dataset of one exercise is shown the same way.
    /// </summary>
    public ExerciseLayout Layout { get; init; } = ExerciseLayout.Table;

    /// <summary>
    /// The release schools see. Null means the exercise has no release yet: its output (if any) is
    /// at the unversioned <see cref="CheckingExerciseBlobPaths.DataPrefix(Guid, Guid?)"/>, which is
    /// where every run before releases existed, and every seeder, wrote it.
    /// </summary>
    public Guid? CurrentReleaseId { get; init; }

    /// <summary>Every release of this exercise, oldest first.</summary>
    public List<CheckingExerciseReleaseDto> Releases { get; init; } = [];

    /// <summary>The release that <see cref="CurrentReleaseId"/> names, or null.</summary>
    public CheckingExerciseReleaseDto? CurrentRelease =>
        CurrentReleaseId is { } id ? Releases.SingleOrDefault(r => r.Id == id) : null;

    /// <summary>The outer window's dates, carried so a caller holding only an exercise can ask
    /// whether its window is running without loading the window again.</summary>
    public DateTime? WindowStart { get; init; }
    public DateTime? WindowEnd { get; init; }

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
    /// waiting for every slot would mean never validating. A retired slot is left out: its file
    /// has been replaced, so a required slot stops blocking once it is retired.
    /// </summary>
    public bool HasRequiredFiles =>
        InUse.Any(d => d.IsComplete) && InUse.Where(d => d.Required).All(d => d.IsComplete);

    /// <summary>The slots that are not retired.</summary>
    private IEnumerable<CheckingWindowDatasetDto> InUse => Datasets.Where(d => !d.Retired);

    /// <summary>
    /// The datasets a run actually reads, in sort order — the complete ones. An empty optional slot
    /// is a file that has not arrived, not a file to fail on, and a run rewrites the exercise's
    /// whole output, so the same exercise is simply re-run when the next file lands. A retired slot
    /// is never read: its file was replaced (the 16-19 revised files replace the first four).
    /// </summary>
    public IReadOnlyList<CheckingWindowDatasetDto> DatasetsToIngest =>
        [.. InUse.Where(d => d.IsComplete).OrderBy(d => d.SortOrder)];

    /// <summary>
    /// The datasets as schools see them: the files that the current release read, or the complete
    /// slots when the exercise has no release. The display reads its schemas from here, not from
    /// the slots, because a slot can hold a newer file that has not been run yet. Its schema (and
    /// the title in it) must not reach schools before its data does.
    /// </summary>
    public IReadOnlyList<CheckingWindowDatasetDto> PublishedDatasets =>
        CurrentRelease is { } release
            ? [.. release.Files.OrderBy(f => f.SortOrder).Select(f => new CheckingWindowDatasetDto
            {
                Id = f.DatasetId,
                Name = f.DatasetName,
                FeedsJourney = f.FeedsJourney,
                Included = f.Included,
                SourceFile = f.SourceFile,
                IngressFile = f.IngressFile,
                IngressFileChecksum = f.IngressFileChecksum,
                SchemaFile = f.SchemaFile,
                SchemaFileChecksum = f.SchemaFileChecksum,
                SortOrder = f.SortOrder
            })]
            : DatasetsToIngest;

    /// <summary>
    /// Validated, and against the files it currently holds. A stamp taken before an ingress file
    /// was swapped is stale, and saying so is the only reason the checksums are stored.
    /// </summary>
    public bool IsValidated =>
        ValidatedAt is not null
        && ValidatedIngressChecksum == CurrentIngressChecksum
        && ValidatedSchemaChecksum == CurrentSchemaChecksum;

    /// <summary>The ingress checksums of the slots in use, in dataset order. Retiring a slot
    /// changes it, so the stamp goes stale until the exercise is run without that file.</summary>
    public string CurrentIngressChecksum => Combine(InUse.OrderBy(d => d.SortOrder).Select(d => d.IngressFileChecksum));

    /// <summary>The schema checksums of the slots in use, in dataset order.</summary>
    public string CurrentSchemaChecksum => Combine(InUse.OrderBy(d => d.SortOrder).Select(d => d.SchemaFileChecksum));

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

    /// <summary>
    /// The journey reads this slot's records. True only for the supplier slots an exercise is
    /// created with (<see cref="WindowDatasets.DefaultsFor"/>) on a pupil-data or results enquiry
    /// exercise. A slot an admin adds later is display only: its records never reach a journey,
    /// so a data share added to a results exercise can never become results a school can query.
    /// Set once, when the slot is created.
    /// </summary>
    public bool FeedsJourney { get; init; }

    /// <summary>
    /// The slot's file has been replaced by another slot's, so no run reads it any more. The slot is
    /// kept, not deleted, because the releases that read it still name it and an admin can put it
    /// back in use. On a 16-19 results enquiry, February retires included, non-included and both
    /// late files; March retires included revised.
    /// </summary>
    public bool Retired { get; set; }

    public int SortOrder { get; init; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}

/// <summary>
/// Which datasets a checking exercise ingests, decided by the window type it sits in and the
/// exercise itself. Pupil data checking takes the supplier's pupil files — two for Post16, because
/// the non-included file has no P_INCL column — and a results enquiry takes one file per source in
/// the results feed, each named by its <see cref="ResultsFileTags"/> tag (#324), from
/// <see cref="ResultsSources"/>.
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
        CheckingWindowType type, CheckingExerciseType? exercise) =>
        exercise switch
        {
            // Pupil data checking and a data share start with no slots. The admin adds the files
            // this window needs: one for KS4, two (included and non-included) for 16-19, or any
            // number for a share. A fixed set of slots guessed the files wrongly as soon as a
            // supplier sent something else, and gave a share a "required" slot it did not need.
            CheckingExerciseType.PupilData => [],
            null => [],
            // The results feed is a fixed set of supplier files, each stamped with its own tag, so
            // a results enquiry still starts with one slot per file.
            CheckingExerciseType.ResultsEnquiry => ResultsEnquiryDefaults(type),
            _ => []
        };

    /// <summary>
    /// Whether a slot an admin adds to an exercise of this type feeds the journey. On pupil data
    /// checking every file is part of the pupils data the journey reads (one file for KS4, two for
    /// 16-19), so yes. On a results enquiry a file with a results source is supplier results, so
    /// yes; a file with no source is display only. A data share has no journey, so no.
    /// </summary>
    public static bool AddedSlotFeedsJourney(CheckingExerciseType? exercise, string? sourceFile) =>
        exercise switch
        {
            CheckingExerciseType.PupilData => true,
            CheckingExerciseType.ResultsEnquiry => !string.IsNullOrEmpty(sourceFile),
            _ => false
        };

    /// <summary>
    /// A supplier slot that belongs to another window type, left behind when the window's type
    /// changed (a KS4 results tag on a window that is now 16-19). Only these are removed when a
    /// window is saved; a slot an admin added is never removed.
    /// </summary>
    public static bool IsStaleSupplierSlot(CheckingWindowType type, CheckingExerciseType? exercise, string name) =>
        DefaultsFor(type, exercise).All(d => d.Name != name)
        && Enum.GetValues<CheckingWindowType>().Any(other => DefaultsFor(other, exercise).Any(d => d.Name == name));

    // One slot per source file, every file of the year from the start: the admin fills each slot
    // when its file arrives and retires a slot when its file is replaced. The slot is named by the
    // tag it stamps, so the admin uploading the files sees the supplier's own file names and a
    // dataset can never be given the wrong tag. KS2 has no results feed, so a results enquiry on a
    // KS2 window gets no slots at all.
    private static IReadOnlyList<CheckingWindowDatasetDto> ResultsEnquiryDefaults(CheckingWindowType type) =>
        [.. ResultsSources.For(type).Select((source, index) => new CheckingWindowDatasetDto
        {
            Name = source.Tag,
            FeedsJourney = true,
            SourceFile = source.Tag,
            // Inclusion is a pupil-data concept: a result row is not included or non-included.
            Included = null,
            // Only the files the exercise starts with are required (16-19: included, non-included
            // and late results 1; KS4: main). The later files land weeks apart and one may never
            // land — an exercise that could not be validated until all of them had arrived would
            // leave a school with no results at all in the meantime.
            Required = source.IsRequired,
            SortOrder = index
        })];
}
