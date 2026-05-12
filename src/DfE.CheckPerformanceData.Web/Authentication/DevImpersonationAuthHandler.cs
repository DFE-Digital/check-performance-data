using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Web.Authentication;

// Authenticates a request from the cypd-dev-impersonation cookie value alone. This is
// the path E2E tests use: they POST to /dev/impersonate/editor to set the cookie, and
// every subsequent request authenticates as a synthetic editor principal — no real
// DfE Sign-In OIDC flow required. Registered only in non-production environments by
// Program.cs.
public sealed class DevImpersonationAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(DevImpersonationConstants.CookieName, out var value)
            || string.IsNullOrEmpty(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var ticket = DevImpersonationTicketBuilder.TryBuild(value);
        return Task.FromResult(ticket is null
            ? AuthenticateResult.NoResult()
            : AuthenticateResult.Success(ticket));
    }
}
