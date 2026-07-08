using DfE.CheckPerformanceData.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DfE.CheckPerformanceData.Web.Admin;

// Authorises the current request against the AdminSectionAccess grid rather than a
// hard-coded role. Every controller under /admin is gated with this attribute (or
// AllowAny variant), so admins configure who reaches what from the Role settings page
// alone — no code change required to widen or narrow access.
//
// Two flavours:
//
//   [RequireAdminSection("app-logs")]                     — the standard case: user must
//                                                           have that specific section granted.
//
//   [RequireAdminSection(AllowAnyGrantedSection = true)]  — used only on the /admin landing
//                                                           page. Any grant at all is enough.
//                                                           Users who reach the landing then
//                                                           see the sidebar filtered to the
//                                                           sections they actually have.
//
// Non-editor / non-admin roles with zero grants get NotFound so the admin surface is
// invisible to them (the URL 404s instead of returning 403 → discourages URL discovery).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAdminSectionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public RequireAdminSectionAttribute() { }
    public RequireAdminSectionAttribute(string sectionKey) => SectionKey = sectionKey;

    /// <summary>The section-key from AdminNavKeys/DefaultAdminAccessSeeder that gates this action.</summary>
    public string? SectionKey { get; init; }

    /// <summary>When true, any granted section is sufficient (used for the /admin landing).</summary>
    public bool AllowAnyGrantedSection { get; init; }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Anonymous users get redirected to sign-in via the normal challenge pipeline,
        // consistent with [Authorize].
        var user = context.HttpContext.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var policy = context.HttpContext.RequestServices.GetService<IAdminAccessPolicy>();
        if (policy is null)
        {
            // Fail-closed: if the policy service isn't wired, nobody gets in.
            context.Result = new NotFoundResult();
            return;
        }

        bool allowed;
        if (AllowAnyGrantedSection)
        {
            var grants = await policy.GetAllGrantsAsync();
            var userRoles = grants
                .Select(g => g.RoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(user.IsInRole)
                .ToList();
            allowed = userRoles.Count > 0;
        }
        else if (!string.IsNullOrWhiteSpace(SectionKey))
        {
            allowed = await policy.CanAccessAsync(user, SectionKey);
        }
        else
        {
            // Misconfigured attribute usage — a section-key or AllowAnyGrantedSection is required.
            throw new InvalidOperationException(
                "RequireAdminSection requires either a section key or AllowAnyGrantedSection = true.");
        }

        if (!allowed)
        {
            // NotFound rather than Forbid — hides the admin surface from users who lack any
            // grant. An admin looking for a routing bug can still see the endpoint in code.
            context.Result = new NotFoundResult();
        }
    }
}
