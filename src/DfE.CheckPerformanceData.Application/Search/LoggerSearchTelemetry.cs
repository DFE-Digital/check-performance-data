using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Search;

// Compile-only stub — the real MEL-templated-placeholder implementation lands in a
// follow-on commit alongside the counter's real impl. RecordSearch throws so the logger
// unit tests fail loudly on the missing behaviour rather than on a missing type. Delete
// this stub when the real implementation lands.
public sealed class LoggerSearchTelemetry(
    ILogger<LoggerSearchTelemetry> logger,
    ISearchZeroResultsCounter counter) : ISearchTelemetry
{
    private readonly ILogger<LoggerSearchTelemetry> _logger = logger;
    private readonly ISearchZeroResultsCounter _counter = counter;

    public void RecordSearch(SearchTelemetryEvent evt) => throw new NotImplementedException();
}
