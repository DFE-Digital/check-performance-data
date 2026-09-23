using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// One exercise a window type starts with: what to call it, what kind it is (null = display-only
/// data share), where it sorts and which dataset slots it takes (#466). The wizard pre-ticks these
/// and the admin may untick any; anything beyond them is added from the Summary page.
/// </summary>
public sealed record ExerciseTemplate(
    string Name,
    string TabName,
    CheckingExerciseType? ExerciseType,
    int SortOrder,
    IReadOnlyList<CheckingWindowDatasetDto> Datasets)
{
    public CheckingExerciseDto ToDto(DateTime startDate, DateTime endDate) => new()
    {
        ExerciseType = ExerciseType,
        Name = Name,
        TabName = TabName,
        StartDate = startDate,
        EndDate = endDate,
        SortOrder = SortOrder,
        Datasets = NewDatasets()
    };

    /// <summary>
    /// Fresh copies of the template's slots: a DTO's slots are mutated by uploads, and a template
    /// may be reused (<see cref="ToDto"/> called twice, or cached later). The one place the five
    /// slot fields are copied, so <see cref="ExerciseDraft.From"/> and <see cref="ToDto"/> cannot
    /// drift apart on which fields survive the copy.
    /// </summary>
    public List<CheckingWindowDatasetDto> NewDatasets() =>
        Datasets.Select(d => new CheckingWindowDatasetDto
        {
            Name = d.Name, Included = d.Included, SourceFile = d.SourceFile,
            Required = d.Required, SortOrder = d.SortOrder
        }).ToList();
}

/// <summary>One offered checkbox on the "which checking exercises" step (#466). Keyed by name — a
/// display-only exercise has no kind to key on.</summary>
public sealed record ExerciseChoice(string Name, CheckingExerciseType? ExerciseType, bool HasFiles);

/// <summary>
/// Which checking exercises a window type runs by default (#319, #466). A starting point rather
/// than a rule — see docs/16-19-window-model.md for why the admin decides.
/// </summary>
/// <remarks>
/// A new <see cref="CheckingExerciseType"/> appears in the wizard from the enum alone, with no row
/// here — the wizard lists every template. This table only decides what starts ticked, so an
/// unmapped window type falling back to pupil data checking is a sensible default rather than a
/// silent failure, and needs no throw.
/// </remarks>
public static class WindowExercises
{
    public const string SummaryAutumnName = "Summary data (Autumn)";
    public const string SummaryTabName = "Summary";
    public const string SummarySlot = "summary";

    public static IReadOnlyList<ExerciseTemplate> DefaultsFor(CheckingWindowType type) =>
        type switch
        {
            // 16-19 runs pupil data checking and results enquiry on different ranges, plus the
            // Autumn summary share — the first of the four Post16 data shares in the year. The
            // other three are added by hand when a year runs them.
            CheckingWindowType.Post16 =>
            [
                Kind(CheckingExerciseType.PupilData, type),
                Kind(CheckingExerciseType.ResultsEnquiry, type),
                new ExerciseTemplate(SummaryAutumnName, SummaryTabName, null, DisplayOnlySortOrderStart,
                    [new CheckingWindowDatasetDto { Name = SummarySlot, Included = null, Required = true, SortOrder = 0 }])
            ],
            _ => [Kind(CheckingExerciseType.PupilData, type)]
        };

    /// <summary>The checkboxes offered on a fresh draft: the window type's templates alone, none
    /// yet ticked or hand-added.</summary>
    public static IReadOnlyList<ExerciseChoice> ChoicesFor(IReadOnlyList<ExerciseTemplate> templates) =>
        templates.Select(t => new ExerciseChoice(t.Name, t.ExerciseType, HasFiles: false)).ToList();

    /// <summary>The checkboxes offered for an existing window: its type's templates first (flagged
    /// when the matching exercise already holds files), then any exercise the admin hand-added that
    /// no template names, in the window's own sort order.</summary>
    public static IReadOnlyList<ExerciseChoice> ChoicesFor(CheckingWindowDto window)
    {
        IReadOnlyList<ExerciseTemplate> templates = DefaultsFor(window.CheckingWindowType);

        IEnumerable<ExerciseChoice> fromTemplates = templates.Select(t => new ExerciseChoice(
            t.Name, t.ExerciseType,
            HasFiles: window.Exercises.SingleOrDefault(e => e.Name == t.Name)?.Datasets.Any(d => d.IsComplete) ?? false));

        IEnumerable<ExerciseChoice> custom = window.Exercises
            .Where(e => templates.All(t => t.Name != e.Name))
            .OrderBy(e => e.SortOrder)
            .Select(e => new ExerciseChoice(e.Name, e.ExerciseType, HasFiles: e.Datasets.Any(d => d.IsComplete)));

        return fromTemplates.Concat(custom).ToList();
    }

    /// <summary>Display order for a kind exercise, and the SortOrder written to its row. Enum order.</summary>
    public static int SortOrderFor(CheckingExerciseType exercise) => (int)exercise;

    /// <summary>
    /// Display-only exercises sort after every kind. An admin may renumber freely. Computed as
    /// max + 1 rather than the member count, which only agrees while the enum stays 0-based and
    /// contiguous. The value is persisted as <c>CheckingExercise.SortOrder</c>, so a summary row
    /// written today ties with a third enum member's sort order if one is added later — the admin
    /// renumbers.
    /// </summary>
    public static readonly int DisplayOnlySortOrderStart =
        Enum.GetValues<CheckingExerciseType>().Cast<int>().Max() + 1;

    /// <summary>
    /// Where a freshly-added display-only exercise sorts (#466 review fix). Not
    /// <c>DisplayOnlySortOrderStart + count(display-only)</c> — that collides after an
    /// add-remove-add, because the count drops back down while the highest SortOrder already used
    /// does not. One past the highest SortOrder any exercise on the window already holds, floored
    /// at <see cref="DisplayOnlySortOrderStart"/> so a display-only exercise never sorts ahead of a
    /// kind exercise on a window that runs none yet.
    /// </summary>
    public static int NextDisplayOnlySortOrder(CheckingWindowDto window) =>
        Math.Max(DisplayOnlySortOrderStart, window.Exercises.Count == 0 ? 0 : window.Exercises.Max(e => e.SortOrder) + 1);

    private static ExerciseTemplate Kind(CheckingExerciseType kind, CheckingWindowType type) =>
        new(CheckingExerciseNames.NameFor(kind), CheckingExerciseNames.TabNameFor(kind), kind,
            SortOrderFor(kind), WindowDatasets.DefaultsFor(type, kind));
}
