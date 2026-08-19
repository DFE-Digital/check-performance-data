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
    public bool Validated { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public bool IsOpen { get; set; }

    /// <summary>
    /// The window's checking exercises, in sort order. A dataset belongs to the exercise that
    /// consumes it, so this is the only route to the window's ingress files. The legacy scalar
    /// IngressFile/SchemaFile properties above are kept for one release for rollback safety and
    /// mirror the first dataset.
    /// </summary>
    public List<CheckingExerciseDto> Exercises { get; set; } = [];

    /// <summary>
    /// Every dataset the window ingests, in exercise order then dataset order. Transitional: the
    /// ingest still runs all of a window's files in one pass and the summary page still lists them
    /// as one table. #316 scopes ingress to the exercise and #319 makes the wizard per-exercise;
    /// both retire this flattening.
    /// </summary>
    public IReadOnlyList<CheckingWindowDatasetDto> AllDatasets =>
        Exercises
            .OrderBy(e => e.SortOrder)
            .SelectMany(e => e.Datasets.OrderBy(d => d.SortOrder))
            .ToList();

    /// <summary>The dataset with this name, held by whichever exercise consumes it.</summary>
    public CheckingWindowDatasetDto? FindDataset(string name) =>
        AllDatasets.SingleOrDefault(d => d.Name == name);
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

