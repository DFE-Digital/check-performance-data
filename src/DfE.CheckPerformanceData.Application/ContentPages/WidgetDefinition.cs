namespace DfE.CheckPerformanceData.Application.ContentPages;

// Catalogue entry for one content widget. <see cref="DefaultPropsJson"/> is a JSON template the
// registry parses into a fresh props object each time a widget is placed (so no two tree nodes ever
// share a mutable node). <see cref="ContributesToNav"/> marks widgets the auto-nav reads (headings).
public sealed record WidgetDefinition(
    string Type,
    string PaletteLabel,
    bool ContributesToNav,
    string DefaultPropsJson);
