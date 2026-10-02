using DfE.CheckPerformanceData.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// A POST made after the sign-in has expired is challenged, and by default DfE Sign-in returns the
// user to the POST's own URL. The browser comes back with a GET, which a POST-only action cannot
// serve, so the user lands on "Page not found". The return address is moved to the page the form
// was on instead.
public sealed class PostChallengeReturnUrlTests
{
    private const string Host = "check-performance-data.education.gov.uk";

    [Fact]
    public void Post_WithSameSiteReferer_ReturnsToThePageTheFormWasOn()
    {
        var request = Request("POST", referer: $"https://{Host}/CheckYourPupilData/3b40f427-4ef4-492b-b1b3-3acda14765e5?tab=results");

        Assert.Equal(
            "/CheckYourPupilData/3b40f427-4ef4-492b-b1b3-3acda14765e5?tab=results",
            PostChallengeReturnUrl.For(request));
    }

    [Fact]
    public void Post_WithNoReferer_ReturnsToTheLandingPage()
    {
        Assert.Equal("/LandingPage", PostChallengeReturnUrl.For(Request("POST", referer: null)));
    }

    // Never an open redirect: a Referer from another site is ignored.
    [Theory]
    [InlineData("https://evil.example/CheckYourPupilData/x")]
    [InlineData("https://check-performance-data.education.gov.uk.evil.example/x")]
    [InlineData("http://check-performance-data.education.gov.uk/CheckYourPupilData/x")]
    [InlineData("not a url")]
    [InlineData("/CheckYourPupilData/x")]
    public void Post_WithARefererThatIsNotThisSite_ReturnsToTheLandingPage(string referer)
    {
        Assert.Equal("/LandingPage", PostChallengeReturnUrl.For(Request("POST", referer)));
    }

    [Fact]
    public void Post_WithRefererOnADifferentPort_ReturnsToTheLandingPage()
    {
        var request = Request("POST", referer: $"https://{Host}:8443/CheckYourPupilData/x");

        Assert.Equal("/LandingPage", PostChallengeReturnUrl.For(request));
    }

    // A GET challenge already returns to a URL that can be fetched again, so it is left alone.
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void NonPost_IsNotChanged(string method)
    {
        Assert.Null(PostChallengeReturnUrl.For(Request(method, referer: $"https://{Host}/CheckYourPupilData/x")));
    }

    private static HttpRequest Request(string method, string? referer)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(Host);
        context.Request.Path = "/CheckYourPupilData/3b40f427-4ef4-492b-b1b3-3acda14765e5/nextstep";
        if (referer is not null) context.Request.Headers.Referer = referer;
        return context.Request;
    }
}
