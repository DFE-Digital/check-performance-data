using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>
/// #938: replaces the supplier rows of the sample schools with generated rows, in the rows that
/// ingress read from one CSV. Ingress then groups, converts and validates every row as before.
/// </summary>
internal static class SampleRows
{
    /// <param name="records">The CSV rows, keyed by column name. Changed in place.</param>
    /// <param name="institutionColumn">The column that holds the school's LAESTAB.</param>
    public static SampleRowSwap Replace(List<IDictionary<string, object>> records, string institutionColumn,
        IReadOnlyList<SampleSchool> schools, ISampleDataGenerator generator, SampleSchemaShape schema)
    {
        // A supplier file can write a LAESTAB with its slash or without it.
        var listed = schools.Select(s => s.Laestab).ToHashSet(StringComparer.Ordinal);
        var removed = records.RemoveAll(r =>
            r.TryGetValue(institutionColumn, out var value) && listed.Contains(LaestabNormaliser.Normalise(value?.ToString())));

        var added = 0;
        foreach (var school in schools)
        {
            foreach (var row in generator.Rows(school, schema))
            {
                var record = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var (key, value) in row)
                    record[key] = value;

                // The rows of a school are grouped by this column. The generator cannot know its
                // name in each file, so ingress writes it.
                record[institutionColumn] = school.Laestab;
                records.Add(record);
                added++;
            }
        }

        return new SampleRowSwap(removed, added);
    }
}

/// <summary>How many supplier rows went, and how many generated rows took their place.</summary>
internal sealed record SampleRowSwap(int Removed, int Added);
