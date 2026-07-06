namespace DfE.CheckPerformanceData.Application.Logging;

// Best-effort ambient HTTP context for the log sink. Implemented in the Web project via
// IHttpContextAccessor; a null-object implementation exists so background-service logs
// (which have no request) still route through the same code path.
public interface ILogRequestContext
{
    string? RequestPath { get; }
    string? UserId { get; }
    string? CorrelationId { get; }
}

public sealed class NullLogRequestContext : ILogRequestContext
{
    public string? RequestPath => null;
    public string? UserId => null;
    public string? CorrelationId => null;
}
