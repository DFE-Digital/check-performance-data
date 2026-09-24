using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>Builds table and download definitions from the schemas uploaded for this exercise.
/// Source rows can be a flat ingress array or a grouped envelope.</summary>
public sealed class ExerciseDisplayService : IExerciseDisplayService
{
    public IReadOnlyList<ExerciseDataset> Define(IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<ExerciseDataset> definitions)
    {
        // Several slots may share one schema — a results enquiry has a slot per supplier file
        // (main, late, revised...) and every file has the same shape and lands in the same
        // per-school blob. They present as one dataset, keyed by the schema's collection.
        definitions = definitions.DistinctBy(d => d.Key).ToList();
        var grouped = definitions.ToDictionary(d => d.Key,
            d => new List<Dictionary<string, string>>());
        foreach (var row in rows)
        {
            var key = DatasetFor(row, definitions);
            if (key is not null && grouped.TryGetValue(key, out var list)) list.Add(row);
        }
        return definitions.Select(d => d with { Rows = grouped[d.Key] }).ToList();
    }

    public ExerciseTableView BuildTable(IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<ExerciseDataset> definitions, string? dataset, string? search, int page, int pageSize)
    {
        var datasets = Define(rows, definitions);
        var selected = datasets.FirstOrDefault(d => d.Key == dataset) ?? datasets[0];
        var searchable = selected.Columns.Where(c => c.Searchable).Select(c => c.Field).ToArray();
        var filtered = string.IsNullOrWhiteSpace(search) ? selected.Rows : selected.Rows
            .Where(row => searchable.Any(field => row.GetValueOrDefault(field, "")
                .Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        var totalPages = (int)Math.Ceiling(filtered.Count / (double)pageSize);
        page = Math.Clamp(page, 0, Math.Max(0, totalPages - 1));
        return new ExerciseTableView(datasets, selected,
            filtered.Skip(page * pageSize).Take(pageSize).Select(Displayed).ToList(), search, page, totalPages);
    }

    /// <summary>Pivots a school's single record into label/value rows. A file with more than one
    /// record is a data fault; the first is shown so the caller can log it rather than lose the tab.</summary>
    public VerticalExerciseView BuildVertical(IReadOnlyList<Dictionary<string, string>> rows,
        ExerciseDataset definition)
    {
        var record = rows.Count > 0 ? rows[0] : null;
        var fields = record is null ? [] : definition.Columns
            .Select(c => new VerticalField(c.Label, Value(record, c.Field))).ToList();
        return new VerticalExerciseView(definition with { Rows = rows }, fields);
    }

    // The supplier sends dates as YYYY-MM-DD; schools read them, on screen and in a download, as
    // DD/MM/YYYY.
    private static readonly HashSet<string> DateFields = new(StringComparer.OrdinalIgnoreCase)
        { "DOB", "DOB_0", "ENTRYDAT" };

    private static string Value(Dictionary<string, string> row, string field)
    {
        var value = row.GetValueOrDefault(field, "");
        return DateFields.Contains(field) ? PupilDateFormatter.ToDisplayDate(value) : value;
    }

    // A copy, so the page's rows are not changed for the next search or download.
    private static Dictionary<string, string> Displayed(Dictionary<string, string> row) =>
        row.ToDictionary(p => p.Key, p => DateFields.Contains(p.Key) ? PupilDateFormatter.ToDisplayDate(p.Value) : p.Value,
            StringComparer.OrdinalIgnoreCase);

    public byte[] Csv(ExerciseDataset dataset)
    {
        var table = new PupilTable(
            dataset.CsvColumns.Select(c => c.Heading).ToList(),
            dataset.Rows.Select(row => (IReadOnlyList<string>)dataset.CsvColumns
                .Select(c => Value(row, c.Field)).ToList()).ToList());
        return PupilCsvGenerator.Generate(table);
    }

    public IReadOnlyList<Dictionary<string, string>> ReadRows(JsonElement root)
    {
        var rows = new List<Dictionary<string, string>>();
        if (root.ValueKind == JsonValueKind.Array)
            Add(root, null);
        else if (root.ValueKind == JsonValueKind.Object)
            foreach (var collection in root.EnumerateObject())
                if (collection.Value.ValueKind == JsonValueKind.Array) Add(collection.Value, collection.Name);
        return rows;

        void Add(JsonElement array, string? dataset)
        {
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var row = element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
                if (dataset is not null) row["DATASET"] = dataset;
                rows.Add(row);
            }
        }
    }

    private static string? DatasetFor(Dictionary<string, string> row, IReadOnlyList<ExerciseDataset> definitions)
    {
        if (row.TryGetValue("DATASET", out var dataset))
            return definitions.FirstOrDefault(d => d.Key.Equals(dataset, StringComparison.OrdinalIgnoreCase))?.Key;
        if (row.ContainsKey("SURNAME_0"))
            return definitions.FirstOrDefault(d => d.Key.Contains("previously-published", StringComparison.OrdinalIgnoreCase))?.Key;
        if (row.TryGetValue("INCLUDED", out var included))
        {
            var matching = definitions.FirstOrDefault(d => d.Included ==
                included.Equals("True", StringComparison.OrdinalIgnoreCase));
            if (matching is not null) return matching.Key;
        }
        if (definitions.Count == 1) return definitions[0].Key;
        // Legacy flat records may lack a dataset marker. Prefer the schema with the most
        // populated fields, but never guess if two definitions score equally.
        var scored = definitions.Select(d => new
            { d.Key, Count = d.Fields.Count(field => row.TryGetValue(field, out var value) && value.Length > 0) })
            .OrderByDescending(d => d.Count).ToList();
        return scored.Count > 0 && scored[0].Count > 0 &&
               (scored.Count == 1 || scored[0].Count > scored[1].Count) ? scored[0].Key : null;
    }

    public ExerciseDataset ParseDefinition(string datasetName, bool? included, JsonElement root)
    {
        var properties = root.GetProperty("properties").EnumerateObject().ToList();
        var key = Text(root, "x-ingress", "collection") ?? datasetName;
        var label = Text(root, "x-display", "section") ?? Text(root, "x-download", "label") ?? datasetName;
        var fileName = Path.GetFileName(Text(root, "x-download", "fileName") ?? $"{key}.csv");
        var columns = properties.Select((property, index) =>
        {
            var display = property.Value.TryGetProperty("x-display", out var value) ? value : default;
            if (display.ValueKind == JsonValueKind.Object && display.TryGetProperty("visible", out var visible) &&
                visible.ValueKind == JsonValueKind.False) return null;
            var order = display.ValueKind == JsonValueKind.Object && display.TryGetProperty("order", out var position)
                ? position.GetInt32() : index;
            var searchable = display.ValueKind == JsonValueKind.Object &&
                display.TryGetProperty("searchable", out var search) && search.GetBoolean();
            return new DisplayColumn(property.Name,
                Text(display, "label") ?? property.Name, order, searchable);
        }).Where(c => c is not null).Select(c => c!).OrderBy(c => c.Order).ToList();
        var csvColumns = properties.Select((property, index) =>
        {
            if (!property.Value.TryGetProperty("x-csv", out var csv))
                return new CsvColumn(property.Name, property.Name, index);
            if (!csv.TryGetProperty("columns", out var positions)) return null;
            // Variant keys are the workbook's, and differ by sheet ("provisional-revised" on the
            // students sheets, "autumn" on the summary). Prefer the named ones, then whatever the
            // property has; an empty map means the field is not exported.
            var variant = positions.TryGetProperty("default", out var standard) ? standard :
                positions.TryGetProperty("provisional-revised", out var provisional) ? provisional :
                positions.ValueKind == JsonValueKind.Object
                    ? positions.EnumerateObject().Select(v => v.Value).FirstOrDefault() : default;
            return variant.ValueKind == JsonValueKind.String
                ? new CsvColumn(property.Name, Text(csv, "heading") ?? property.Name,
                    ExcelOrder(variant.GetString()!)) : null;
        }).Where(c => c is not null).Select(c => c!).OrderBy(c => c.Order).ToList();
        // x-display.layout in a schema is ignored: the exercise sets the layout (see ExerciseLayout).
        return new ExerciseDataset(key, label, fileName, columns, csvColumns, [], included,
            new HashSet<string>(properties.Select(p => p.Name), StringComparer.OrdinalIgnoreCase));
    }

    private static string? Text(JsonElement element, params string[] path)
    {
        foreach (var part in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element)) return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static int ExcelOrder(string column)
    {
        var order = 0;
        foreach (var letter in column) order = order * 26 + letter - 'A' + 1;
        return order;
    }
}
