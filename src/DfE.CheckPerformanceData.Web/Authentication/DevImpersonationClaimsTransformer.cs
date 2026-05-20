using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authentication;

namespace DfE.CheckPerformanceData.Web.Authentication;

// Reads the cypd-dev-impersonation cookie on every request and overlays the editor role
// onto the authenticated principal. Registered only in non-production environments by
// Program.cs; the class itself is environment-agnostic so it's testable in isolation.
//
// Idempotency: TransformAsync clones the principal before mutating so the original (held
// by the auth handler and reused across the request pipeline) is not modified.
public sealed class DevImpersonationClaimsTransformer(IHttpContextAccessor httpContextAccessor)
    : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return Task.FromResult(principal);
        }

        if (!context.Request.Cookies.TryGetValue(DevImpersonationConstants.CookieName, out var value))
        {
            return Task.FromResult(principal);
        }

        if (value != DevImpersonationConstants.EditorValue
            && value != DevImpersonationConstants.UserValue
            && value != DevImpersonationConstants.AdminValue)
        {
            return Task.FromResult(principal);
        }

        var clone = principal.Clone();

        // Check across ALL identities — DfE Sign-In's ClaimsEnrichmentService adds
        // roles on a separate "DfeSignIn" identity via AddIdentity, so role claims
        // rarely live on the first (OIDC) identity. Use principal.IsInRole which
        // iterates every identity rather than scanning one in isolation.
        var hasEditorRole = clone.IsInRole(WikiConstants.EditorRole);
        var hasAdminRole = clone.IsInRole(WikiConstants.AdminRole);

        if (value == DevImpersonationConstants.EditorValue && !hasEditorRole)
        {
            var target = clone.Identities.FirstOrDefault();
            if (target is null)
            {
                target = new ClaimsIdentity(authenticationType: DevImpersonationConstants.Scheme);
                clone.AddIdentity(target);
            }
            target.AddClaim(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }
        else if (value == DevImpersonationConstants.UserValue && hasEditorRole)
        {
            // Remove from every identity that carries the role — defensive against the
            // case where the same role claim is duplicated across identities.
            foreach (var id in clone.Identities)
            {
                var claim = id.FindFirst(c =>
                    c.Type == ClaimTypes.Role && c.Value == WikiConstants.EditorRole);
                if (claim is not null)
                {
                    id.RemoveClaim(claim);
                }
            }
        }
        else if (value == DevImpersonationConstants.AdminValue && !hasAdminRole)
        {
            // Admin overlay is additive only — it stamps cypmd_admin and never touches
            // the editor role. Mirrors the editor branch's identity-creation pattern.
            var target = clone.Identities.FirstOrDefault();
            if (target is null)
            {
                target = new ClaimsIdentity(authenticationType: DevImpersonationConstants.Scheme);
                clone.AddIdentity(target);
            }
            target.AddClaim(new Claim(ClaimTypes.Role, WikiConstants.AdminRole));
        }

        return Task.FromResult(clone);
    }
}
