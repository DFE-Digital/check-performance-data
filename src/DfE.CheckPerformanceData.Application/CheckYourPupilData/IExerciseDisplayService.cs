using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Turns a school's ingested JSON plus the schemas it was validated against into something a view
/// can render. All of it is pure: the caller supplies the bytes, this decides what they mean.
/// </summary>
public interface IExerciseDisplayService
{
    /// <summary>Read a dataset definition out of one uploaded schema.</summary>
    ExerciseDataset ParseDefinition(string datasetName, bool? included, JsonElement schemaRoot);

    /// <summary>Read a school's rows from a flat array or a grouped envelope.</summary>
    IReadOnlyList<Dictionary<string, string>> ReadRows(JsonElement root);

    /// <summary>Split the rows across the definitions they belong to.</summary>
    IReadOnlyList<ExerciseDataset> Define(IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<ExerciseDataset> definitions);

    /// <summary>The searched, paged table view.</summary>
    ExerciseTableView BuildTable(IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<ExerciseDataset> definitions, string? dataset, string? search, int page, int pageSize);

    /// <summary>The one-record view, pivoted to label/value rows.</summary>
    VerticalExerciseView BuildVertical(IReadOnlyList<Dictionary<string, string>> rows, ExerciseDataset definition);

    /// <summary>The dataset as a CSV, in the schema's export order.</summary>
    byte[] Csv(ExerciseDataset dataset);
}
