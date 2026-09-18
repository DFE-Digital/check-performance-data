using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed record StudentDisplayColumn(string Field, string Label, int Order, bool Searchable);
public sealed record StudentCsvColumn(string Field, string Heading, int Order);
public sealed record StudentDataset(string Key, string Label, string FileName,
    IReadOnlyList<StudentDisplayColumn> Columns, IReadOnlyList<StudentCsvColumn> CsvColumns,
    IReadOnlyList<Dictionary<string, string>> Rows);
public sealed record Post16StudentsView(IReadOnlyList<StudentDataset> Datasets, StudentDataset Selected,
    IReadOnlyList<Dictionary<string, string>> Rows, string? Search, int Page, int TotalPages);

/// <summary>Reads the display contract from the Post-16 schemas rather than maintaining a second
/// list of table columns in Razor. Source rows can be today's flat ingress array or the grouped
/// envelope defined by post16-ingress.json.</summary>
public static class Post16StudentDisplay
{
    private static readonly string[] DatasetKeys =
        ["students-included", "students-non-included", "students-previously-published"];
    private static readonly Lazy<IReadOnlyList<StudentDataset>> Definitions = new(LoadDefinitions);

    public static IReadOnlyList<StudentDataset> Define(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var grouped = Definitions.Value.ToDictionary(d => d.Key,
            d => new List<Dictionary<string, string>>());
        foreach (var row in rows)
        {
            var key = DatasetFor(row);
            if (key is not null && grouped.TryGetValue(key, out var list)) list.Add(row);
        }
        return Definitions.Value.Select(d => d with { Rows = grouped[d.Key] }).ToList();
    }

    public static Post16StudentsView Build(IReadOnlyList<Dictionary<string, string>> rows,
        string? dataset, string? search, int page, int pageSize)
    {
        var datasets = Define(rows);
        var selected = datasets.FirstOrDefault(d => d.Key == dataset) ?? datasets[0];
        var searchable = selected.Columns.Where(c => c.Searchable).Select(c => c.Field).ToArray();
        var filtered = string.IsNullOrWhiteSpace(search) ? selected.Rows : selected.Rows
            .Where(row => searchable.Any(field => row.GetValueOrDefault(field, "")
                .Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        var totalPages = (int)Math.Ceiling(filtered.Count / (double)pageSize);
        page = Math.Clamp(page, 0, Math.Max(0, totalPages - 1));
        return new Post16StudentsView(datasets, selected,
            filtered.Skip(page * pageSize).Take(pageSize).ToList(), search, page, totalPages);
    }

    public static string Value(Dictionary<string, string> row, string field)
    {
        var value = row.GetValueOrDefault(field, "");
        return field is "DOB" or "DOB_0" ? PupilDateFormatter.ToDisplayDate(value) : value;
    }

    public static byte[] Csv(StudentDataset dataset)
    {
        var table = new DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns.PupilTable(
            dataset.CsvColumns.Select(c => c.Heading).ToList(),
            dataset.Rows.Select(row => (IReadOnlyList<string>)dataset.CsvColumns
                .Select(c => row.GetValueOrDefault(c.Field, "")).ToList()).ToList());
        return PupilCsvGenerator.Generate(table);
    }

    public static IReadOnlyList<Dictionary<string, string>> ReadRows(JsonElement root)
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

    private static string? DatasetFor(Dictionary<string, string> row)
    {
        if (row.TryGetValue("DATASET", out var dataset))
            return DatasetKeys.Contains(dataset) ? dataset : null;
        if (row.ContainsKey("SURNAME_0")) return "students-previously-published";
        if (!row.ContainsKey("SURNAME") || !row.ContainsKey("FORENAMES") ||
            row.ContainsKey("GNUMBER") || row.ContainsKey("LearningAimReference")) return null;
        if (row.ContainsKey("P_INCL") || row.GetValueOrDefault("INCLUDED", "") == "True")
            return "students-included";
        return "students-non-included";
    }

    private static IReadOnlyList<StudentDataset> LoadDefinitions()
    {
        var assembly = typeof(Post16StudentDisplay).Assembly;
        return DatasetKeys.Select(key =>
        {
            using var stream = assembly.GetManifestResourceStream(
                $"DfE.CheckPerformanceData.Web.Data.Ingress.schema.post16.{key}.json")
                ?? throw new InvalidOperationException($"Missing Post-16 display schema: {key}");
            using var schema = JsonDocument.Parse(stream);
            var root = schema.RootElement;
            var properties = root.GetProperty("properties").EnumerateObject().ToList();
            var columns = properties
                .Where(p => p.Value.GetProperty("x-display").GetProperty("visible").GetBoolean())
                .Select(p => new StudentDisplayColumn(p.Name,
                    p.Value.GetProperty("x-display").GetProperty("label").GetString()!,
                    p.Value.GetProperty("x-display").GetProperty("order").GetInt32(),
                    p.Value.GetProperty("x-display").TryGetProperty("searchable", out var searchable)
                        && searchable.GetBoolean()))
                .OrderBy(c => c.Order).ToList();
            var csvColumns = properties.Select(p =>
                {
                    var csv = p.Value.GetProperty("x-csv");
                    var positions = csv.GetProperty("columns");
                    var variant = positions.TryGetProperty("default", out var standard) ? standard :
                        positions.TryGetProperty("provisional-revised", out var provisional) ? provisional : default;
                    return variant.ValueKind == JsonValueKind.String
                        ? new StudentCsvColumn(p.Name, csv.GetProperty("heading").GetString()!, ExcelOrder(variant.GetString()!))
                        : null;
                }).Where(c => c is not null).Select(c => c!).OrderBy(c => c.Order).ToList();
            return new StudentDataset(key, root.GetProperty("x-display").GetProperty("section").GetString()!,
                root.GetProperty("x-download").GetProperty("fileName").GetString()!, columns, csvColumns, []);
        }).ToList();
    }

    private static int ExcelOrder(string column)
    {
        var order = 0;
        foreach (var letter in column) order = order * 26 + letter - 'A' + 1;
        return order;
    }
}
