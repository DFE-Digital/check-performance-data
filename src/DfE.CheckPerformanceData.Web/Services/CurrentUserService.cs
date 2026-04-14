using System.Security.Claims;
using DfE.CheckPerformanceData.Application;

namespace DfE.CheckPerformanceData.Web.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public string? UserId =>
        httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "system";

    public string? DisplayName =>
        httpContextAccessor.HttpContext?.User?.Identity?.Name
        ?? "System";
}
