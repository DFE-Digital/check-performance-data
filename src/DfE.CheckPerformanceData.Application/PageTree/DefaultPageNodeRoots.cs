namespace DfE.CheckPerformanceData.Application.PageTree;

/// The folder nodes seeded at the root of the page tree and always permitted as page-tree roots
/// even though some of their URLs are also claimed by existing controllers (the page catch-all is
/// evaluated last, so those pages fall through to it when no other route matches).
///
/// Each root carries a **hardcoded Guid** so its identity is stable across every environment. The
/// content-staging import matches nodes by GUID first, so pinning the seed roots guarantees a
/// bundle from environment A ends up updating the same rows in environment B rather than
/// colliding on path and being adopted via the path-fallback branch. The path fallback still
/// exists as a safety net for legacy environments that were seeded before the pinning went in
/// (or for editor-authored pages that share a segment with a bundle item), but the seeded roots
/// don't need it.
public static class DefaultPageNodeRoots
{
    public static readonly IReadOnlyList<(Guid Id, string Segment, string Title)> All =
    [
        (new Guid("00000000-cd94-4a01-8f01-000000000001"), "support",  "Support"),
        (new Guid("00000000-cd94-4a01-8f01-000000000002"), "wiki",     "Wiki"),
        (new Guid("00000000-cd94-4a01-8f01-000000000003"), "help",     "Help"),
        (new Guid("00000000-cd94-4a01-8f01-000000000004"), "guidance", "Guidance"),
    ];

    /// <summary>Stable Guid for the auto-seeded /help/not-found content page.</summary>
    public static readonly Guid HelpNotFoundId = new("00000000-cd94-4a01-8f01-00000000000f");

    public static readonly IReadOnlySet<string> Segments =
        new HashSet<string>(All.Select(r => r.Segment), StringComparer.OrdinalIgnoreCase);
}
