namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>A column as the schema asks for it to be shown.</summary>
public sealed record DisplayColumn(string Field, string Label, int Order, bool Searchable);

/// <summary>A column as the schema asks for it to be exported, in the workbook's own order.</summary>
public sealed record CsvColumn(string Field, string Heading, int Order);

/// <summary>
/// How an exercise's tab shows its data. An admin sets it on the exercise. The schemas do not
/// set it, so all datasets of one exercise are shown the same way.
/// </summary>
public enum ExerciseLayout
{
    /// <summary>Many records: dataset selector, search, paged table.</summary>
    Table,
    /// <summary>One record per school, pivoted to label/value rows. No search, no paging.</summary>
    Vertical
}

/// <summary>One schema's worth of a school's data: how to show it, how to export it, and its rows.</summary>
public sealed record ExerciseDataset(string Key, string Label, string FileName,
    IReadOnlyList<DisplayColumn> Columns, IReadOnlyList<CsvColumn> CsvColumns,
    IReadOnlyList<Dictionary<string, string>> Rows, bool? Included, IReadOnlySet<string> Fields);

public sealed record VerticalField(string Label, string Value);

/// <summary>The one-record view. The dataset carries the rows, so the CSV download serves the same
/// definition the page rendered.</summary>
public sealed record VerticalExerciseView(ExerciseDataset Dataset, IReadOnlyList<VerticalField> Fields)
{
    public string Key => Dataset.Key;
    public string Label => Dataset.Label;
    public string FileName => Dataset.FileName;
}

/// <summary>The many-record view: every dataset for the selector, the selected one, and its page.</summary>
public sealed record ExerciseTableView(IReadOnlyList<ExerciseDataset> Datasets, ExerciseDataset Selected,
    IReadOnlyList<Dictionary<string, string>> Rows, string? Search, int Page, int TotalPages);

/// <summary>One tab on Check Your Pupil Data.</summary>
public sealed record ExerciseTab(WindowManagement.CheckingDataExercise Exercise,
    IReadOnlyList<Dictionary<string, string>> Rows, bool HasData)
{
    /// <summary>Set when a dataset's schema could not be read. The tab says so rather than
    /// showing a table the schema was meant to shape.</summary>
    public bool SchemaUnavailable { get; init; }

    public ExerciseTableView? Table { get; init; }
    public VerticalExerciseView? Vertical { get; init; }

    /// <summary>The datasets the tab was built from: the live release's, or the slots when there
    /// is no release. The raw download reads the same ones.</summary>
    public IReadOnlyList<WindowManagement.CheckingWindowDatasetDto> PublishedDatasets { get; init; } = [];

    /// <summary>The raw columns, for an exercise with no schemas at all.</summary>
    public IReadOnlyList<string> Columns => Rows.SelectMany(r => r.Keys).Distinct().ToList();
}
