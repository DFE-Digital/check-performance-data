namespace DfE.CheckPerformanceData.Application.PageTree;

/// The folder nodes seeded at the root of the page tree and always permitted as page-tree roots
/// even though some of their URLs are also claimed by existing controllers (the page catch-all is
/// evaluated last, so those pages fall through to it when no other route matches).
public static class DefaultPageNodeRoots
{
    public static readonly IReadOnlyList<(string Segment, string Title)> All =
    [
        ("support",  "Support"),
        ("wiki",     "Wiki"),
        ("help",     "Help"),
        ("guidance", "Guidance"),
    ];

    public static readonly IReadOnlySet<string> Segments =
        new HashSet<string>(All.Select(r => r.Segment), StringComparer.OrdinalIgnoreCase);
}
