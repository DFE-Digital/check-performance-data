namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed record ContentBlockLocation(string Url, string PageTitle);

/// <summary>
/// Content blocks are embedded in MVC pages by key, so a search hit must be mapped back
/// to the page (and section anchor) that renders it. This is the single place that knows
/// which page owns which block-key namespace.
/// </summary>
public static class ContentBlockLocations
{
    private const string Ks4Prefix = "guidance-ks4-2026-";
    private const string Ks4Url = "/guidance/2026-ks4-june-checking-exercise";
    private const string Ks4Title = "How to check your performance measures data: KS4 June 2026";

    // KS4 blocks that are not navigable sections (no anchor) — link to the page top.
    private static readonly HashSet<string> Ks4NonSection =
        new(StringComparer.Ordinal) { "title", "intro", "published", "nav", "caption" };

    public static ContentBlockLocation? Resolve(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        if (key.StartsWith("home-", StringComparison.Ordinal))
            return new ContentBlockLocation("/", "Home");

        if (key.StartsWith("guidance-landing-", StringComparison.Ordinal))
            return new ContentBlockLocation("/guidance", "CYPMD help and support");

        if (key.StartsWith(Ks4Prefix, StringComparison.Ordinal))
        {
            var rest = key[Ks4Prefix.Length..];
            if (Ks4NonSection.Contains(rest))
                return new ContentBlockLocation(Ks4Url, Ks4Title);

            // Section body ("…-key-dates") and its heading ("…-key-dates-heading") both
            // deep-link to the section anchor.
            var anchor = rest.EndsWith("-heading", StringComparison.Ordinal)
                ? rest[..^"-heading".Length]
                : rest;
            return new ContentBlockLocation($"{Ks4Url}#{anchor}", Ks4Title);
        }

        // Unknown/unmapped block — not surfaced in search results.
        return null;
    }
}
