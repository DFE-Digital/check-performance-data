namespace DfE.CheckPerformanceData.Application.Logging;

// Read-model for the admin logs page. Mirrors the persistence entity shape but lives in
// Application so callers don't need to reference Persistence just to render a row.
public sealed class AppLogDto
{
    public required long Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public int EventId { get; init; }
    public string? StateJson { get; init; }
    public string? RequestPath { get; init; }
    public string? UserId { get; init; }
    public string? CorrelationId { get; init; }
}
