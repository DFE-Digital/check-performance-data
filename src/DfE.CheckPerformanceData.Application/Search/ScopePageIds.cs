namespace DfE.CheckPerformanceData.Application.Search;

// The pages a Search or Search results widget is limited to, stored by page id as a
// comma-separated list ("scopePageIds"). Storing ids rather than paths means a widget keeps its
// pages when they are renamed or moved.
public static class ScopePageIds
{
    // The distinct ids in the list, in the order given. Anything that is not an id is dropped.
    public static IReadOnlyList<Guid> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var ids = new List<Guid>();
        foreach (var part in raw.Split(','))
        {
            if (Guid.TryParse(part.Trim(), out var id) && !ids.Contains(id))
                ids.Add(id);
        }
        return ids;
    }

    // The canonical stored form, or null when the list holds no id.
    public static string? Normalise(string? raw) => Join(Parse(raw).Select(id => id.ToString()));

    // The tokens that name these pages on a search link, comma-separated, or null for none.
    public static string? ToPageTokens(string? raw) => Join(Parse(raw).Select(PageToken.For));

    private static string? Join(IEnumerable<string> parts)
    {
        var joined = string.Join(',', parts);
        return joined.Length == 0 ? null : joined;
    }
}
