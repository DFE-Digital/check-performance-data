using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.Logging;
using DfE.CheckPerformanceData.Application.UnitTests.Common;
using DfE.CheckPerformanceData.Web.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// A signed-in user who reaches an admin URL without the grant is answered 404 (the surface is
// hidden, see the attribute's remarks) — and since #612 that refusal is also logged, because
// OWASP A01/A09 ask that access-control failures leave a trace with enough user context to
// investigate and to alert on. These facts pin the trace: one Warning, the one AccessRefused
// event, the user id + section + path in the message, and nothing personal beyond the id. The
// 404 and the challenge are pinned too, so the logging cannot change the responses.
public sealed class RequireAdminSectionAttributeTests
{
    private const string UserId = "4f1c9d0e-user-id";
    private const string Email = "head@school.example";
    private const string GivenName = "Priya";
    private const string Section = "app-logs";
    private const string Path = "/admin/system-administration/logs";

    private readonly IAdminAccessPolicy _policy = Substitute.For<IAdminAccessPolicy>();
    private readonly RecordingLogger<RequireAdminSectionAttribute> _logger = new();

    [Fact]
    public async Task SignedInUserWithoutTheGrant_Gets404_AndOneAccessRefusedWarning()
    {
        _policy.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), Section).Returns(false);
        var context = Context(SignedIn());

        await new RequireAdminSectionAttribute(Section).OnAuthorizationAsync(context);

        Assert.IsType<NotFoundResult>(context.Result);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(AccessRefusalLog.AccessRefused, entry.EventId);
        Assert.Contains(UserId, entry.Message);
        Assert.Contains(Section, entry.Message);
        Assert.Contains(Path, entry.Message);
    }

    // The negative half is the real pin: a template that added {Email} "for debuggability"
    // would put personal data in Logit while every positive assertion stayed green. The query
    // string is dropped too — it can carry search terms.
    [Fact]
    public async Task TheWarning_CarriesNothingPersonalButTheUserId()
    {
        _policy.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), Section).Returns(false);
        var context = Context(SignedIn());

        await new RequireAdminSectionAttribute(Section).OnAuthorizationAsync(context);

        var entry = Assert.Single(_logger.Entries);
        Assert.DoesNotContain(Email, entry.Message);
        Assert.DoesNotContain(GivenName, entry.Message);
        Assert.DoesNotContain("secret", entry.Message);
    }

    [Fact]
    public async Task SignedInUserWithTheGrant_IsAllowed_AndNothingIsLogged()
    {
        _policy.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), Section).Returns(true);
        var context = Context(SignedIn());

        await new RequireAdminSectionAttribute(Section).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task AnonymousUser_IsChallenged_AndNothingIsLogged()
    {
        var context = Context(new ClaimsPrincipal(new ClaimsIdentity()));

        await new RequireAdminSectionAttribute(Section).OnAuthorizationAsync(context);

        Assert.IsType<ChallengeResult>(context.Result);
        Assert.Empty(_logger.Entries);
        await _policy.DidNotReceive().CanAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>());
    }

    // The /admin landing page uses the any-granted-section flavour; a refusal there has no
    // single section key, so the message carries a fixed marker instead.
    [Fact]
    public async Task LandingPageFlavour_RefusesAUserWhoseRolesHoldNoGrant_AndLogsTheMarker()
    {
        _policy.GetAllGrantsAsync().Returns(new List<RoleSectionAccessGrant>
        {
            new("cypmd_admin", "dashboard"),
            new("cypmd_content_access_user", "content-pages")
        });
        var context = Context(SignedIn(roles: ["some_other_role"]));

        await new RequireAdminSectionAttribute { AllowAnyGrantedSection = true }.OnAuthorizationAsync(context);

        Assert.IsType<NotFoundResult>(context.Result);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(AccessRefusalLog.AccessRefused, entry.EventId);
        Assert.Contains(AccessRefusalLog.AnyGrantedSection, entry.Message);
        Assert.Contains(UserId, entry.Message);
    }

    private AuthorizationFilterContext Context(ClaimsPrincipal user)
    {
        var services = new ServiceCollection()
            .AddSingleton(_policy)
            .AddSingleton<ILogger<RequireAdminSectionAttribute>>(_logger)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = services };
        http.Request.Path = Path;
        http.Request.QueryString = new QueryString("?search=secret");
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(action, []);
    }

    private static ClaimsPrincipal SignedIn(string[]? roles = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, UserId),
            new(ClaimTypes.Email, Email),
            new(ClaimTypes.GivenName, GivenName)
        };
        foreach (var role in roles ?? [])
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
