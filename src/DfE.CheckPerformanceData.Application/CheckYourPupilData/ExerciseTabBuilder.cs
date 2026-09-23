using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface IExerciseTabBuilder
{
    Task<IReadOnlyList<ExerciseTab>> BuildAsync(CheckingWindowDto window, string? laestab,
        string? selectedDataset, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the Check Your Pupil Data tabs for one window and one school: which exercises are
/// visible, what each holds for this school, and how its schemas ask to be shown.
/// </summary>
/// <remarks>
/// An empty result means this window's exercises draw no tabs, which is every window configured
/// before #466. The page falls back to its inclusion tabs, so a window that has not been given
/// exercises keeps working exactly as it did.
/// </remarks>
public sealed class ExerciseTabBuilder(ICheckingDataReader reader, IExerciseDisplayService display,
    TimeProvider clock, ILogger<ExerciseTabBuilder> logger) : IExerciseTabBuilder
{
    public async Task<IReadOnlyList<ExerciseTab>> BuildAsync(CheckingWindowDto window, string? laestab,
        string? selectedDataset, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var now = clock.GetLocalNow().DateTime;

        // A row with no tab name draws no tab. That is what keeps the fallback reachable.
        var visible = window.Exercises
            .Where(e => !string.IsNullOrWhiteSpace(e.TabName))
            .Select(e => new CheckingDataExercise(e.Id, window.Id,
                e.Name ?? e.TabName!, e.TabName!, e.TabOrder, e.ExerciseType, window.KeyStage,
                e.IsEnabled, e.VisibleFrom, e.VisibleUntil, window.StartDate, window.EndDate,
                e.StartDate, e.EndDate, e.ReplacesCheckingExerciseId, e.UsesExerciseStorage, e.DisplayOnly))
            .Where(e => e.IsVisible(now))
            .OrderBy(e => e.TabOrder).ThenBy(e => e.Id)
            .ToList();

        var tabs = new List<ExerciseTab>();
        foreach (var exercise in visible)
        {
            if (string.IsNullOrWhiteSpace(laestab))
            {
                tabs.Add(new ExerciseTab(exercise, [], false));
                continue;
            }

            var bytes = await reader.ReadAsync(exercise, laestab, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                // The tab stays. A school seeing an empty tab knows the exercise exists and holds
                // nothing for them yet; a missing tab tells them nothing at all.
                tabs.Add(new ExerciseTab(exercise, [], false));
                continue;
            }

            using var json = JsonDocument.Parse(bytes);
            var rows = display.ReadRows(json.RootElement);

            var source = window.Exercises.Single(e => e.Id == exercise.Id);
            var definitions = new List<ExerciseDataset>();
            var schemaUnavailable = false;
            foreach (var dataset in source.DatasetsToIngest)
            {
                var schemaBytes = await reader.ReadSchemaAsync(window.Id, dataset.SchemaFile, cancellationToken);
                if (schemaBytes is null || schemaBytes.Length == 0)
                {
                    schemaUnavailable = true;
                    break;
                }
                using var schema = JsonDocument.Parse(schemaBytes);
                definitions.Add(display.ParseDefinition(dataset.Name, dataset.Included, schema.RootElement));
            }

            // The schema decides the layout, not the tab name and not the exercise type. An
            // exercise with no schemas falls back to a raw column-per-key table.
            var hasSchemas = source.DatasetsToIngest.Count > 0 && !schemaUnavailable;
            var vertical = hasSchemas && definitions.All(d => d.Layout == ExerciseLayout.Vertical);

            if (hasSchemas && !vertical && definitions.Any(d => d.Layout == ExerciseLayout.Vertical))
            {
                // One layout per exercise. A mix is a schema-authoring fault; the table renderer
                // can show every dataset, so it wins, and the odd one out is logged not hidden.
                logger.LogWarning("Exercise {ExerciseId} mixes vertical and table schemas; rendering as a table",
                    exercise.Id);
            }
            if (vertical && rows.Count > 1)
            {
                logger.LogWarning("Exercise {ExerciseId} holds {Count} records for {Laestab}; a vertical layout shows the first",
                    exercise.Id, rows.Count, laestab);
            }

            tabs.Add(new ExerciseTab(exercise, rows, true)
            {
                SchemaUnavailable = schemaUnavailable,
                Table = hasSchemas && !vertical
                    ? display.BuildTable(rows, definitions, selectedDataset, search, page, pageSize)
                    : null,
                Vertical = vertical ? display.BuildVertical(rows, definitions[0]) : null
            });
        }

        return tabs;
    }
}
