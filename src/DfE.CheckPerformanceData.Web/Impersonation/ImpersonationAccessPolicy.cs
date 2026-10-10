using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Authentication;

namespace DfE.CheckPerformanceData.Web.Impersonation;

public static class ImpersonationAccessPolicy
{
    public const string AdminRole = "cypmd_admin";
    public const string ImpersonationRole = "cypmd_impersonation";
    public static bool CanAccess(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
        && !user.Identities.Any(i => i.AuthenticationType == DevImpersonationConstants.Scheme)
        && user.IsInRole(AdminRole) && user.IsInRole(ImpersonationRole);
}
