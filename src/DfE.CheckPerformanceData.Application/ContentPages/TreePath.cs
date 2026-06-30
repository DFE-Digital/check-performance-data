namespace DfE.CheckPerformanceData.Application.ContentPages;

// Wire format for a tree path: each step as "column.index", steps joined by "-" (e.g. "0.2-1.0").
// The editor round-trips a position through a form field with this; Parse rejects malformed input
// rather than risk mis-targeting an edit.
public static class TreePath
{
    public static string Format(IReadOnlyList<TreeStep> path) =>
        string.Join("-", path.Select(step => $"{step.Column}.{step.Index}"));

    public static IReadOnlyList<TreeStep> Parse(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new FormatException("A tree path must have at least one step.");

        var steps = new List<TreeStep>();
        foreach (var segment in value.Split('-'))
        {
            var parts = segment.Split('.');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var column)
                || !int.TryParse(parts[1], out var index)
                || column < 0 || index < 0)
            {
                throw new FormatException($"Invalid tree-path segment '{segment}'.");
            }

            steps.Add(new TreeStep(column, index));
        }

        return steps;
    }
}
