using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Authentication;

/// <summary>
/// Where "sign out" goes. Real DfE auth — any authenticated identity that is not the synthetic
/// dev-impersonation scheme — signs out through DfE Sign-In; an impersonation-only session
/// deletes its cookie instead. Shared by the header (_Layout) and Check your pupil data's
/// "No, I'd like to sign out of this service" answer (AB#298317), so the two can never disagree.
/// </summary>
/// <remarks>
/// Don't compare against a specific scheme name such as "Cookies": the effective primary
/// identity's AuthenticationType varies across the DfE Sign-In cookie, the OIDC pipeline and the
/// "DfeSignIn" enrichment identity. Never target /dev/impersonate/user — that keeps a synthetic
/// principal signed in.
/// </remarks>
public static class SignOutLink
{
    public const string ImpersonationClearPath = "/dev/impersonate/clear";

    public static string For(HttpContext context, IUrlHelper url)
    {
        var user = context.User;
        var hasRealDfeAuth = user.Identity?.IsAuthenticated == true
            && user.Identities.Any(i => i.IsAuthenticated
                && i.AuthenticationType != DevImpersonationConstants.Scheme);

        return hasRealDfeAuth
            ? url.Action("DfeSignOut", "Account") ?? "/Account/DfeSignOut"
            : ImpersonationClearPath;
    }
}
