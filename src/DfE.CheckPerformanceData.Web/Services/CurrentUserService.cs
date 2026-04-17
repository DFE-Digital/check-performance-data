using System.Security.Claims;
using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.Web.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public string UserId =>
        httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "system";

    public string DisplayName =>
        httpContextAccessor.HttpContext?.User.Identity?.Name
        ?? "System";
}
