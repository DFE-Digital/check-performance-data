using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The request log must say who made a request and where a redirect sent them. Without both, a
// 302 from an auth challenge cannot be told from a 302 from the controller, which is what made a
// PROD "Continue does nothing" report impossible to settle from logs alone.
public sealed class RequestLogEnrichmentTests
{
    [Fact]
    public void RedirectTarget_RelativeLocation_KeepsThePathAndDropsTheQuery()
    {
        var response = Response(302, "/WhatToChange/3b40f427-4ef4-492b-b1b3-3acda14765e5?x=1");

        Assert.Equal("/WhatToChange/3b40f427-4ef4-492b-b1b3-3acda14765e5", RequestLogEnrichment.RedirectTarget(response));
    }

    [Fact]
    public void RedirectTarget_AbsoluteLocation_KeepsOriginAndPathButNeverTheQuery()
    {
        // The OIDC authorize URL carries state and nonce in its query. They must not reach the log.
        var response = Response(302,
            "https://oidc.signin.education.gov.uk/auth?client_id=cypd&state=CfDJ8secret&nonce=abc");

        Assert.Equal("https://oidc.signin.education.gov.uk/auth", RequestLogEnrichment.RedirectTarget(response));
    }

    [Fact]
    public void RedirectTarget_RelativeLocationWithFragment_DropsTheFragment()
    {
        var response = Response(302, "/CheckYourPupilData/abc#results");

        Assert.Equal("/CheckYourPupilData/abc", RequestLogEnrichment.RedirectTarget(response));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    public void RedirectTarget_NonRedirectStatus_IsNull(int status)
    {
        Assert.Null(RequestLogEnrichment.RedirectTarget(Response(status, "/somewhere")));
    }

    [Fact]
    public void RedirectTarget_RedirectWithNoLocation_IsNull()
    {
        Assert.Null(RequestLogEnrichment.RedirectTarget(Response(302, null)));
    }

    [Fact]
    public void UserId_SignedInUser_IsTheNameIdentifierClaim()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "CF6E2682-995F-4E93-95CD-F06700BA4C09")], "Cookies"))
        };

        Assert.Equal("CF6E2682-995F-4E93-95CD-F06700BA4C09", RequestLogEnrichment.UserId(context));
    }

    [Fact]
    public void UserId_AnonymousUser_IsNull()
    {
        Assert.Null(RequestLogEnrichment.UserId(new DefaultHttpContext()));
    }

    [Fact]
    public void UserId_UnauthenticatedIdentityCarryingAClaim_IsNull()
    {
        // No authentication type means the identity is not authenticated, whatever claims it holds.
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "someone")]))
        };

        Assert.Null(RequestLogEnrichment.UserId(context));
    }

    private static HttpResponse Response(int status, string? location)
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = status;
        if (location is not null) context.Response.Headers.Location = location;
        return context.Response;
    }
}
