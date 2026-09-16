using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// robots.txt is served from a controller rather than wwwroot so its content can follow the host
// environment. It is the second layer behind the X-Robots-Tag header (#444): the header removes
// pages already indexed, this stops a compliant crawler fetching them again. On its own it would
// be worse than nothing — a crawler that is told not to fetch a page never sees the noindex, so
// a stale entry stays in the index.
public sealed class RobotsControllerTests
{
    private static string Body(string environment)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);

        var result = Assert.IsType<ContentResult>(new RobotsController(env).Index());
        Assert.Equal("text/plain", result.ContentType);
        return result.Content!;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Review")]
    [InlineData("QA")]
    [InlineData("Preproduction")]
    public void NonProduction_DisallowsEverything(string environment)
    {
        var body = Body(environment);

        Assert.Contains("User-agent: *", body);
        Assert.Contains("Disallow: /", body);
        Assert.DoesNotContain("Allow: /", body);
    }

    [Fact]
    public void Production_AllowsCrawling()
    {
        var body = Body(Environments.Production);

        Assert.Contains("User-agent: *", body);
        Assert.Contains("Allow: /", body);
        Assert.DoesNotContain("User-agent: *\nDisallow: /", body);
    }

    // The service is behind DfE Sign-in and holds nothing an AI training corpus should have.
    // Compliant training crawlers honour a robots.txt disallow; this is the accepted control.
    [Theory]
    [InlineData("GPTBot")]
    [InlineData("ClaudeBot")]
    [InlineData("Google-Extended")]
    [InlineData("CCBot")]
    public void Production_DisallowsAiTrainingCrawlers(string agent)
    {
        var body = Body(Environments.Production);

        Assert.Contains($"User-agent: {agent}\nDisallow: /", body);
    }
}
