namespace DfE.CheckPerformanceData.Application.Analytics;

// Transform from a browser-reported instant search to the same (SearchEventDto, result rows)
// pair the site-search mapper produces, so both surfaces land in one table and the dashboard
// reads them through one query layer.
//
// The counting rule is the only real decision here: a "section" hit increments ResultsSections
// rather than ResultsPages. A section is a heading on the page the person was already reading,
// not a document they could have found any other way — counting it as a page would inflate
// every per-page figure on the dashboard and make an on-page search look like site traffic.
public static class InstantSearchEventMapper
{
    public const string SectionKind = "section";
    public const string PageKind = "page";

    public static (SearchEventDto Event, IReadOnlyList<SearchEventResultDto> Results) From(
        InstantSearchTelemetryEvent evt, string sessionId)
    {
        var results = new List<SearchEventResultDto>(evt.Shown.Count);
        var pages = 0;
        var sections = 0;

        foreach (var hit in evt.Shown)
        {
            var kind = string.Equals(hit.Kind, SectionKind, StringComparison.Ordinal)
                ? SectionKind
                : PageKind;

            if (kind == SectionKind) sections++;
            else pages++;

            results.Add(new SearchEventResultDto(
                Position: hit.Position,
                ResultKind: kind,
                ResultKey: hit.Key,
                // A typeahead has no ts_rank to report: the browser knows the order it showed
                // but not the score behind it. Zero rather than a fabricated number, so a rank
                // sort on the dashboard cannot mistake a suggestion for a scored search hit.
                Rank: 0f));
        }

        var dto = new SearchEventDto(
            OccurredAtUtc: evt.UtcTimestamp,
            SessionId: sessionId,
            QueryRaw: evt.QueryRaw,
            QueryNormalised: evt.QueryNormalised,
            Scope: evt.Scope,
            ResultsPages: pages,
            ResultsBlocks: 0,
            LatencyMs: evt.LatencyMs,
            Results: results,
            ResultsSections: sections,
            Surface: evt.Surface,
            HostPath: evt.HostPath,
            SelectedKey: evt.SelectedKey,
            SelectedPosition: evt.SelectedPosition);

        return (dto, results);
    }
}
