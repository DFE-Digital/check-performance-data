using System.Threading.Channels;
using DfE.CheckPerformanceData.Application.Search;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Analytics;

// Composite ISearchTelemetry decorator that fans one RecordSearch call into two paths:
//
//   1. Structured logging — delegates verbatim to the inner LoggerSearchTelemetry. The
//      inner is injected as the CONCRETE type (not the interface) so DI can register it
//      distinctly and this decorator does not self-recurse if the container ever picked
//      the decorator's own interface binding.
//   2. Analytics enqueue — maps the event into a SearchEventDto and TryWrite()s it onto
//      the bounded channel that the SearchEventWriter background service drains. Never
//      throws, never blocks. If the channel is full, increments the drop counter and
//      writes a warn line so operators can see the shed under load.
//
// The session id comes from ISearchAnalyticsSessionProvider (Web-side concrete reads the
// ASP.NET Session server-side — never a cookie or HTML-comment read; defends against
// form-tampering paths that inject a foreign session id). The abstraction keeps the
// Application project free of the ASP.NET Core reference. Background / non-request
// callers (provider returns null) skip the channel write entirely — there is no session
// to attribute — but still fire the inner logger since an unattributable event is still
// worth structured logging.
public sealed class SinkAndLogSearchTelemetry : ISearchTelemetry
{
    private readonly ChannelWriter<SearchEventDto> _writer;
    private readonly LoggerSearchTelemetry _inner;
    private readonly ISearchAnalyticsDroppedCounter _droppedCounter;
    private readonly ILogger<SinkAndLogSearchTelemetry> _logger;
    private readonly ISearchAnalyticsSessionProvider _sessionProvider;

    public SinkAndLogSearchTelemetry(
        ChannelWriter<SearchEventDto> writer,
        LoggerSearchTelemetry inner,
        ISearchAnalyticsDroppedCounter droppedCounter,
        ILogger<SinkAndLogSearchTelemetry> logger,
        ISearchAnalyticsSessionProvider sessionProvider)
    {
        _writer = writer;
        _inner = inner;
        _droppedCounter = droppedCounter;
        _logger = logger;
        _sessionProvider = sessionProvider;
    }

    public void RecordSearch(SearchTelemetryEvent evt)
    {
        var sessionId = _sessionProvider.GetSessionId();

        // With a live session we can attribute the event; without one (background caller
        // or a request whose session middleware never ran) the analytics store has no
        // key to file the row against, so skip the enqueue rather than write an
        // unattributable row.
        if (!string.IsNullOrEmpty(sessionId))
        {
            var (dto, _) = SearchEventMapper.From(evt, sessionId);
            if (!_writer.TryWrite(dto))
            {
                _droppedCounter.Increment();
                _logger.LogWarning(
                    "Search analytics event dropped: channel full (SearchId={SearchId})",
                    evt.SearchId);
            }
        }

        // Delegate to the inner regardless — structured logging must not be gated on
        // sink success, and a background-thread caller without a session still deserves
        // its summary log line.
        _inner.RecordSearch(evt);
    }
}
