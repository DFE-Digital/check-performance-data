using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public sealed record RecordedLog(LogLevel Level, EventId EventId, string Message);

// Records level, event id and the rendered message of every log call. Asserting those through an
// NSubstitute ILogger is awkward (the generic TState), and the access-refusal tests need all
// three: the level is the alerting contract, the event id is the one name Logit searches for, and
// the message is where personal data would leak if anyone "improved" a template.
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLog> _entries = [];

    public IReadOnlyList<RecordedLog> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Add(new RecordedLog(logLevel, eventId, formatter(state, exception)));
}
