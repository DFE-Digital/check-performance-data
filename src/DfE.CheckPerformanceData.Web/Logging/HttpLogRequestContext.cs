using DfE.CheckPerformanceData.Application.Logging;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Logging;

// Web-project implementation of ILogRequestContext: pulls the ambient path / user / trace id
// off IHttpContextAccessor. When no request is in flight (background service, hosted worker),
// every property is null — the sink handles that fine.
public sealed class HttpLogRequestContext(IHttpContextAccessor accessor) : ILogRequestContext
{
    public string? RequestPath => accessor.HttpContext?.Request?.Path.Value;
    public string? UserId => accessor.HttpContext?.User?.Identity?.Name;
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
