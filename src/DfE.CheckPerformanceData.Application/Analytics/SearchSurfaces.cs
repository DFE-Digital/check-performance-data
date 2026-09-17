namespace DfE.CheckPerformanceData.Application.Analytics;

// The search surfaces a recorded event can come from. String constants rather than an enum
// because the value is persisted as text, crosses an HTTP boundary from the browser, and is
// read back in raw SQL — one spelling in one place keeps those three honest.
public static class SearchSurfaces
{
    // A submitted search: /search, or a results widget on a content page.
    public const string Site = "site";

    // Typeahead over the whole site or a section of it. Choosing a suggestion opens a page.
    public const string Instant = "instant";

    // Typeahead over the sections of the page the widget sits on. Choosing a suggestion
    // moves to a heading on that page rather than opening anything.
    public const string InstantPage = "instant-page";

    public static readonly IReadOnlyList<string> All = [Site, Instant, InstantPage];

    public static bool IsKnown(string? surface) =>
        surface is not null && All.Contains(surface, StringComparer.Ordinal);

    // True for the two typeahead surfaces — the ones whose rows carry a selection.
    public static bool IsInstant(string? surface) =>
        string.Equals(surface, Instant, StringComparison.Ordinal)
        || string.Equals(surface, InstantPage, StringComparison.Ordinal);
}
