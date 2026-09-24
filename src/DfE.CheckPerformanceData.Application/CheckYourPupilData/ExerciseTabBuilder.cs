using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface IExerciseTabBuilder
{
    /// <param name="activeTab">
    /// The <see cref="ExerciseTab.Key"/> the dataset, search and page belong to. Every other tab
    /// starts on its first dataset, with no search, on its first page. Null applies them to every
    /// tab, which is how a link from before tab keys existed still works.
    /// </param>
    Task<IReadOnlyList<ExerciseTab>> BuildAsync(CheckingWindowDto window, string? laestab, string? activeTab,
        string? selectedDataset, string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// The raw JSON a school may download for one tab, unshaped by any schema. With no release it is
    /// the exercise's one output file, byte for byte. With a release it is one JSON object with a
    /// property per dataset (named by the dataset), each holding that dataset's file as written.
    /// Null when the school has no data in the exercise.
    /// </summary>
    Task<byte[]?> ReadRawAsync(ExerciseTab tab, string laestab, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the Check Your Pupil Data tabs for one window and one school: which exercises are
/// visible, what each holds for this school, and how its schemas ask to be shown.
/// </summary>
/// <remarks>
/// An empty result means no exercise of this window is visible now. The page then falls back to
/// its inclusion tabs.
/// </remarks>
public sealed class ExerciseTabBuilder(ICheckingDataReader reader, IExerciseDisplayService display,
    TimeProvider clock, ILogger<ExerciseTabBuilder> logger) : IExerciseTabBuilder
{
    public async Task<IReadOnlyList<ExerciseTab>> BuildAsync(CheckingWindowDto window, string? laestab,
        string? activeTab, string? selectedDataset, string? search, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var now = clock.GetLocalNow().DateTime;
        var noun = LearnerNoun.For(window.CheckingWindowType);

        var visible = window.Exercises
            .Select(e => new CheckingDataExercise(e.Id, window.Id,
                e.Name ?? e.TabName, e.TabName, e.TabOrder, e.ExerciseType, window.KeyStage,
                e.IsEnabled, e.VisibleFrom, e.VisibleUntil, window.StartDate, window.EndDate,
                e.StartDate, e.EndDate, e.ReplacesCheckingExerciseId, e.UsesExerciseStorage, e.DisplayOnly,
                e.CurrentReleaseId))
            .Where(e => e.IsVisible(now))
            .OrderBy(e => e.TabOrder).ThenBy(e => e.Id)
            .ToList();

        var tabs = new List<ExerciseTab>();
        foreach (var exercise in visible)
        {
            var source = window.Exercises.Single(e => e.Id == exercise.Id);
            var published = source.PublishedDatasets;
            var shapes = ShapesFor(exercise, source.Layout);

            if (string.IsNullOrWhiteSpace(laestab))
            {
                tabs.AddRange(shapes.Select(shape => Empty(exercise, shape, published, noun)));
                continue;
            }

            // With a release, each dataset has its own file, so every row's dataset is known.
            // Without one, the exercise has one merged file, and the display service works out
            // each row's dataset from the row itself. A release whose merged file is its one
            // dataset's file is read from the merged file, and every row belongs to that dataset.
            var files = new List<(CheckingWindowDatasetDto? Dataset, IReadOnlyList<Dictionary<string, string>> Rows)>();
            if (!HasDatasetFiles(exercise, published))
            {
                var bytes = await reader.ReadAsync(exercise, laestab, cancellationToken);
                if (bytes is { Length: > 0 })
                {
                    using var json = JsonDocument.Parse(bytes);
                    files.Add((MergedFileDataset(exercise, published), display.ReadRows(json.RootElement)));
                }
            }
            else
            {
                foreach (var dataset in published)
                {
                    var bytes = await reader.ReadDatasetAsync(exercise, dataset.Id, laestab, cancellationToken);
                    if (bytes is not { Length: > 0 }) continue;
                    using var json = JsonDocument.Parse(bytes);
                    files.Add((dataset, display.ReadRows(json.RootElement)));
                }
            }

            if (files.Count == 0)
            {
                // The tab stays. A school seeing an empty tab knows the exercise exists and holds
                // nothing for them yet; a missing tab tells them nothing at all.
                tabs.AddRange(shapes.Select(shape => Empty(exercise, shape, published, noun)));
                continue;
            }

            var definitions = new List<ExerciseDataset>();
            var keys = new Dictionary<Guid, string>();
            var schemaUnavailable = false;
            foreach (var dataset in published)
            {
                var schemaBytes = await reader.ReadSchemaAsync(window.Id, dataset.SchemaFile, cancellationToken);
                if (schemaBytes is null || schemaBytes.Length == 0)
                {
                    schemaUnavailable = true;
                    break;
                }
                using var schema = JsonDocument.Parse(schemaBytes);
                var definition = display.ParseDefinition(dataset.Name, dataset.Included, schema.RootElement);
                definitions.Add(definition);
                keys[dataset.Id] = definition.Key;
            }

            // A row from a dataset's own file is marked with that dataset's key, which the display
            // service matches before anything else. No row is ever shown under a dataset it did not
            // come from.
            var rows = new List<Dictionary<string, string>>();
            foreach (var (dataset, fileRows) in files)
            {
                if (dataset is not null && keys.TryGetValue(dataset.Id, out var key))
                {
                    foreach (var row in fileRows) row["DATASET"] = key;
                }
                rows.AddRange(fileRows);
            }

            // The exercise decides the layout, not its schemas. An exercise with no schemas
            // falls back to a raw column-per-key table.
            var hasSchemas = published.Count > 0 && !schemaUnavailable;
            var vertical = hasSchemas && source.Layout == ExerciseLayout.Vertical;

            // A vertical exercise shows one record: its first dataset's. Rows with no dataset mark
            // come from a merged file written before releases, and are all candidates.
            var verticalRows = vertical
                ? rows.Where(r => !r.TryGetValue("DATASET", out var key) || key == definitions[0].Key).ToList()
                : [];
            if (vertical && verticalRows.Count > 1)
            {
                logger.LogWarning("Exercise {ExerciseId} holds {Count} records for {Laestab}; a vertical layout shows the first",
                    exercise.Id, verticalRows.Count, laestab);
            }

            foreach (var shape in shapes)
            {
                // An inclusion tab holds only its own pupils, and names its datasets for itself, so
                // its heading, its download and its file in the download-all zip are its own. It
                // keeps the names and the surname, forename order the pupil CSVs had before
                // exercises: "pupil-include" and "pupil-non-include". The controller adds the
                // school and window to the name.
                var tabRows = shape.Included is { } included
                    ? rows.Where(r => PupilInclusion.IsIncluded(r) == included)
                        .OrderBy(r => r.GetValueOrDefault("SURNAME", ""))
                        .ThenBy(r => r.GetValueOrDefault("FORENAME", ""))
                        .ToList()
                    : rows;
                var tabDefinitions = shape.Included is null
                    ? definitions
                    : definitions.Select(d => d with
                    {
                        Label = $"{d.Label} {shape.Suffix}",
                        FileName = definitions.Count == 1
                            ? $"pupil-{shape.FileSuffix}.csv"
                            : $"{Path.GetFileNameWithoutExtension(d.FileName)}-{shape.FileSuffix}{Path.GetExtension(d.FileName)}"
                    }).ToList();
                var active = activeTab is null || activeTab == shape.Key;

                tabs.Add(new ExerciseTab(exercise, tabRows, true)
                {
                    Key = shape.Key,
                    Label = shape.Label,
                    Included = shape.Included,
                    LearnerNoun = noun,
                    PublishedDatasets = published,
                    SchemaUnavailable = schemaUnavailable,
                    Table = hasSchemas && !vertical
                        ? display.BuildTable(tabRows, tabDefinitions, active ? selectedDataset : null,
                            active ? search : null, active ? page : 0, pageSize)
                        : null,
                    Vertical = vertical ? display.BuildVertical(verticalRows, definitions[0]) : null
                });
            }
        }

        return tabs;
    }

    // One tab per exercise, except under InclusionTabs: an included tab and a non-included tab.
    private static IReadOnlyList<TabShape> ShapesFor(CheckingDataExercise exercise, ExerciseLayout layout) =>
        layout == ExerciseLayout.InclusionTabs
            ?
            [
                new TabShape($"exercise-{exercise.Id}-included", $"{exercise.TabName} Included",
                    true, "Included", "include"),
                new TabShape($"exercise-{exercise.Id}-nonincluded", $"{exercise.TabName} Non Included",
                    false, "Non Included", "non-include")
            ]
            : [new TabShape($"exercise-{exercise.Id}", exercise.TabName, null, "", "")];

    private static ExerciseTab Empty(CheckingDataExercise exercise, TabShape shape,
        IReadOnlyList<CheckingWindowDatasetDto> published, LearnerNoun noun) =>
        new(exercise, [], false)
        {
            Key = shape.Key, Label = shape.Label, Included = shape.Included, LearnerNoun = noun,
            PublishedDatasets = published
        };

    private sealed record TabShape(string Key, string Label, bool? Included, string Suffix, string FileSuffix);

    public async Task<byte[]?> ReadRawAsync(ExerciseTab tab, string laestab, CancellationToken cancellationToken)
    {
        if (!HasDatasetFiles(tab.Exercise, tab.PublishedDatasets))
            return await reader.ReadAsync(tab.Exercise, laestab, cancellationToken);

        // Each dataset's file is copied in as written, under the dataset's name.
        using var output = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            var any = false;
            writer.WriteStartObject();
            foreach (var dataset in tab.PublishedDatasets)
            {
                var bytes = await reader.ReadDatasetAsync(tab.Exercise, dataset.Id, laestab, cancellationToken);
                if (bytes is not { Length: > 0 }) continue;
                using var json = JsonDocument.Parse(bytes);
                writer.WritePropertyName(dataset.Name);
                json.RootElement.WriteTo(writer);
                any = true;
            }
            writer.WriteEndObject();
            if (!any) return null;
        }
        return output.ToArray();
    }

    // Per-dataset files exist only in a release, and only in one published since they were
    // introduced: a release from before then records no dataset ids and wrote only the merged file.
    // A release whose merged file is its one dataset's file does not write a copy of it.
    private static bool HasDatasetFiles(CheckingDataExercise exercise, IReadOnlyList<CheckingWindowDatasetDto> published) =>
        exercise.CurrentReleaseId is not null && published.Count > 0 && published.All(d => d.Id != Guid.Empty)
        && !CheckingExerciseBlobPaths.MergedFileIsDatasetFile([.. published.Select(d => d.FeedsJourney)]);

    // The one dataset every row of the merged file came from, when the release says so.
    private static CheckingWindowDatasetDto? MergedFileDataset(CheckingDataExercise exercise,
        IReadOnlyList<CheckingWindowDatasetDto> published) =>
        exercise.CurrentReleaseId is not null && published is [{ Id: var id } only] && id != Guid.Empty
        && CheckingExerciseBlobPaths.MergedFileIsDatasetFile([only.FeedsJourney])
            ? only
            : null;
}
