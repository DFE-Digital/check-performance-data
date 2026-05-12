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
            && value != DevImpersonationConstants.UserValue)
        {
            return Task.FromResult(principal);
        }

        var clone = principal.Clone();
        var identity = clone.Identities.FirstOrDefault();
        if (identity is null)
        {
            identity = new ClaimsIdentity(authenticationType: DevImpersonationConstants.Scheme);
            clone.AddIdentity(identity);
        }

        var editorClaim = identity.Claims.FirstOrDefault(c =>
            c.Type == ClaimTypes.Role && c.Value == WikiConstants.EditorRole);

        if (value == DevImpersonationConstants.EditorValue && editorClaim is null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }
        else if (value == DevImpersonationConstants.UserValue && editorClaim is not null)
        {
            identity.RemoveClaim(editorClaim);
        }

        return Task.FromResult(clone);
    }
}
