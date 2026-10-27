namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// One successful ingress run of a checking exercise: the files it read and the output it wrote.
/// </summary>
/// <remarks>
/// A run writes a new output set under its own prefix
/// (<see cref="CheckingExerciseBlobPaths.ReleaseDataPrefix"/>) and never over the previous one. The
/// exercise's <see cref="CheckingExerciseDto.CurrentReleaseId"/> says which release schools see. So a
/// full replacement of the data (the 16-19 "revised" results, for example) keeps every earlier
/// release, and an admin can put an earlier release back live without a re-upload.
/// </remarks>
public sealed class CheckingExerciseReleaseDto
{
    public Guid Id { get; init; }

    /// <summary>1 for the first release of the exercise, then 2, 3, and so on.</summary>
    public int Number { get; init; }

    /// <summary>When the run finished, in UTC.</summary>
    public DateTime PublishedAt { get; init; }

    /// <summary>The admin who started the run. Empty when the run had no signed-in user.</summary>
    public string PublishedBy { get; init; } = string.Empty;

    /// <summary>The number of per-school files the run wrote.</summary>
    public int FilesWritten { get; init; }

    /// <summary>The file and schema of each dataset slot that the run read, in slot order.</summary>
    public List<CheckingExerciseReleaseFileDto> Files { get; init; } = [];
}

/// <summary>
/// One dataset slot as a release read it. This is a copy, not a reference to the slot: an admin
/// may upload a new file into the slot after the run, and the release must still say which file
/// made its output and which schema shapes its display.
/// </summary>
public sealed class CheckingExerciseReleaseFileDto
{
    /// <summary>The slot this file was read from. Names the dataset's own output folder.</summary>
    public Guid DatasetId { get; init; }
    public required string DatasetName { get; init; }

    /// <summary>The slot fed the journey when the run read it.</summary>
    public bool FeedsJourney { get; init; }
    public bool? Included { get; init; }
    public string? SourceFile { get; init; }
    public string IngressFile { get; init; } = string.Empty;
    public string IngressFileChecksum { get; init; } = string.Empty;
    public string SchemaFile { get; init; } = string.Empty;
    public string SchemaFileChecksum { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}
