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
    private const string SyntheticEmail = "dev-impersonation-user@education.gov.uk";

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
            new(ClaimTypes.Name, SyntheticName),
            new(ClaimTypes.Email, SyntheticEmail)
        };

        if (cookieValue == DevImpersonationConstants.EditorValue)
        {
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }

        // Admin implies editor (one-way hierarchy). Stamping both role claims here
        // means every existing [Authorize(Roles = WikiConstants.EditorRole)] endpoint
        // automatically accepts an admin principal without any per-endpoint change.
        // The reverse is NOT true — an editor cookie never grants admin access.
        if (cookieValue == DevImpersonationConstants.AdminValue)
        {
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.AdminRole));
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }

        var identity = new ClaimsIdentity(claims, DevImpersonationConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, DevImpersonationConstants.Scheme);
    }
}
