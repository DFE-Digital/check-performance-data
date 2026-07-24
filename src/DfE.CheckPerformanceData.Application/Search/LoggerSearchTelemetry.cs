using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Search;

// Default ISearchTelemetry sink. Every RecordSearch call fans one SearchTelemetryEvent out
// to four MEL log tiers:
//
//   Info    — one summary line per request. Carries SearchId, QueryRaw, per-corpus result
//             counts, filter-exclusion count, and the latency breakdown so a single log
//             entry answers "what happened to this request?" without joining lines.
//   Debug/  — one line per kept hit carrying the per-field rank breakdown.
//   Info    — Debug by default; promoted to Info when ISearchDebugOptions.ShowSearchDebug
//             is true (i.e. the CMS:SearchDebugOn setting is on). This lets ops flip
//             visibility from the admin settings page without lowering the process-wide
//             log level.
//   Debug/  — one line per FilterExclusion carrying the exclusion kind + row key.
//   Info    — Same Debug-to-Info promotion rule as per-hit lines.
//   Warn    — zero-result line, gated on evt.Hits.Count == 0 and preceded by a counter bump.
//
// All user-controlled strings (QueryRaw, QueryNormalised, RowId, Url, Title, RowKey, Kind)
// are passed as DISCRETE MEL templated-placeholder arguments so Serilog's JSON formatter
// escapes embedded newlines / control characters on the argument side. The format
// strings are plain compile-time constants — no string-interpolation prefix and no
// concatenation of user input at any log-emitting call site — and the field labels
// appear as plain literal text OUTSIDE the {} placeholders, so log queries and human
// readers see field=value pairs while the runtime never splices adversarial input into
// the format string itself.
public sealed class LoggerSearchTelemetry(
    ILogger<LoggerSearchTelemetry> logger,
    ISearchZeroResultsCounter counter,
    ISearchDebugOptions debug) : ISearchTelemetry
{
    public void RecordSearch(SearchTelemetryEvent evt)
    {
        if (evt.Hits.Count == 0)
        {
            counter.Increment();
            logger.LogWarning(
                "Search returned zero results SearchId={SearchId} QueryRaw={QueryRaw} QueryNormalised={QueryNormalised} Scope={Scope}",
                evt.SearchId,
                evt.QueryRaw,
                evt.QueryNormalised,
                evt.Scope ?? "(none)");
        }

        var resultsPages = 0;
        var resultsBlocks = 0;
        foreach (var hit in evt.Hits)
        {
            if (hit.Corpus == "page") resultsPages++;
            else if (hit.Corpus == "block") resultsBlocks++;
        }

        logger.LogInformation(
            "Search completed SearchId={SearchId} QueryRaw={QueryRaw} ResultsPages={ResultsPages} ResultsBlocks={ResultsBlocks} FilterExclusionsCount={FilterExclusionsCount} LatencyMsTotal={LatencyMsTotal}ms LatencyMsPages={LatencyMsPages}ms LatencyMsBlocks={LatencyMsBlocks}ms",
            evt.SearchId,
            evt.QueryRaw,
            resultsPages,
            resultsBlocks,
            evt.FilterExclusions.Count,
            evt.LatencyMsTotal,
            evt.LatencyMsPages,
            evt.LatencyMsBlocks);

        // Debug knob resolved once per RecordSearch call so a mid-call toggle flip cannot
        // split a single event's per-hit lines across two log levels.
        var breadcrumbLevel = debug.ShowSearchDebug ? LogLevel.Information : LogLevel.Debug;

        foreach (var hit in evt.Hits)
        {
            logger.Log(
                breadcrumbLevel,
                "Search {SearchId} hit {Corpus}:{RowId} url={Url} rank_total={RankTotal:F4} rank_kw={RankKeywords:F4} rank_title={RankTitle:F4} rank_sub={RankSubtitle:F4} rank_body={RankBody:F4} rank_value={RankValue:F4}",
                evt.SearchId,
                hit.Corpus,
                hit.RowId,
                hit.Url,
                hit.RankTotal,
                hit.RankKeywords,
                hit.RankTitle,
                hit.RankSubtitle,
                hit.RankBody,
                hit.RankValue);
        }

        foreach (var excl in evt.FilterExclusions)
        {
            logger.Log(
                breadcrumbLevel,
                "Search {SearchId} excluded {Corpus}:{RowKey} by {FilterKind}",
                evt.SearchId,
                excl.Corpus,
                excl.RowKey,
                excl.Kind);
        }
    }
}
