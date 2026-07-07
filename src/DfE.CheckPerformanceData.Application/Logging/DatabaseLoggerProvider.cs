using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Logging;

// ILoggerProvider that hands out DatabaseLogger instances, one per category. Registered in
// Program.cs alongside the console provider — this sink is additive and never replaces the
// normal log destinations.
[ProviderAlias("Database")]
public sealed class DatabaseLoggerProvider(
    AppLogChannel channel,
    AppLogSinkOptions options,
    ILogRequestContext? requestContext = null) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new DatabaseLogger(
            name, channel, options, requestContext ?? new NullLogRequestContext()));

    public void Dispose() => _loggers.Clear();
}

public sealed class DatabaseLogger(
    string category,
    AppLogChannel channel,
    AppLogSinkOptions options,
    ILogRequestContext requestContext) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel < options.MinLevel || logLevel == LogLevel.None) return false;
        return !ShouldSkipCategory(category, options.SkipCategories);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var dto = new AppLogDto
        {
            Id = 0,
            Timestamp = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Category = category,
            Message = formatter(state, exception),
            Exception = exception?.ToString(),
            EventId = eventId.Id,
            StateJson = SerializeState(state),
            RequestPath = requestContext.RequestPath,
            UserId = requestContext.UserId,
            CorrelationId = requestContext.CorrelationId
        };

        // TryWrite never blocks — the bounded channel drops the oldest queued row on overflow.
        channel.Channel.Writer.TryWrite(dto);
    }

    private static bool ShouldSkipCategory(string cat, IReadOnlyList<string> skip)
    {
        for (int i = 0; i < skip.Count; i++)
            if (cat.StartsWith(skip[i], StringComparison.Ordinal))
                return true;
        return false;
    }

    // Structured state for ILogger.Log usually implements IReadOnlyList<KeyValuePair<string,object?>>
    // — the placeholder name/value pairs from the message template plus a synthesised {OriginalFormat}.
    // Round-trip those into a small JSON object so the admin page (or ops) can query by property.
    private static string? SerializeState<TState>(TState state)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs && pairs.Count > 0)
        {
            var dict = new Dictionary<string, object?>(pairs.Count, StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                dict[pair.Key] = pair.Value?.ToString();
            }
            return System.Text.Json.JsonSerializer.Serialize(dict);
        }
        return null;
    }
}
