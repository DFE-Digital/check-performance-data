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

    /// <param name="returnUrl">
    /// AB#298317: where the impersonation-clear path should land afterwards, instead of its default
    /// Referer-based redirect. Only ever relevant to the impersonation branch — the layout's normal
    /// call omits it, so /dev/impersonate/clear keeps returning to whatever page linked to it.
    /// Check your pupil data passes "/" here, because its own Referer (the page the school was
    /// signing out from) requires authentication and would otherwise bounce the now-signed-out
    /// browser straight into a fresh DfE Sign-In challenge instead of showing it has signed out.
    /// </param>
    public static string For(HttpContext context, IUrlHelper url, string? returnUrl = null)
    {
        var user = context.User;
        var hasRealDfeAuth = user.Identity?.IsAuthenticated == true
            && user.Identities.Any(i => i.IsAuthenticated
                && i.AuthenticationType != DevImpersonationConstants.Scheme);

        if (hasRealDfeAuth)
        {
            return url.Action("DfeSignOut", "Account") ?? "/Account/DfeSignOut";
        }

        return returnUrl is null
            ? ImpersonationClearPath
            : $"{ImpersonationClearPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
