using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authentication;

namespace DfE.CheckPerformanceData.Web.Authentication;

// Pure mapping from cypd-dev-impersonation cookie value to an AuthenticationTicket.
// Split out from DevImpersonationAuthHandler so it's directly unit-testable without
// the IOptionsMonitor + ILoggerFactory plumbing the handler base class requires.
public static class DevImpersonationTicketBuilder
{
    private const string SyntheticNameIdentifier = "dev-impersonation-user";
    private const string SyntheticName = "Dev impersonation user";

    public static AuthenticationTicket? TryBuild(string cookieValue)
    {
        if (cookieValue != DevImpersonationConstants.EditorValue
            && cookieValue != DevImpersonationConstants.UserValue
            && cookieValue != DevImpersonationConstants.AdminValue)
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, SyntheticNameIdentifier),
            new(ClaimTypes.Name, SyntheticName)
        };

        if (cookieValue == DevImpersonationConstants.EditorValue)
        {
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }

        // Admin role is orthogonal to the editor role — the admin cookie stamps only
        // cypmd_admin and never the editor role. A user who needs both gets both
        // assignments at the DSI level.
        if (cookieValue == DevImpersonationConstants.AdminValue)
        {
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.AdminRole));
        }

        var identity = new ClaimsIdentity(claims, DevImpersonationConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, DevImpersonationConstants.Scheme);
    }
}
