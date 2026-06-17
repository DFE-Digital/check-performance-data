using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Web.Notify;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

public class EmailLinkGeneratorTests
{
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly HttpContext _httpContext = Substitute.For<HttpContext>();
    private readonly ILogger<EmailLinkGenerator> _logger = Substitute.For<ILogger<EmailLinkGenerator>>();

    public EmailLinkGeneratorTests()
    {
        var request = Substitute.For<HttpRequest>();
        request.Scheme.Returns("https");
        request.Host.Returns(new HostString("example.gov.uk"));
        request.PathBase.Returns(new PathString(""));
        request.IsHttps.Returns(true);
        _httpContext.Request.Returns(request);
        _httpContextAccessor.HttpContext.Returns(_httpContext);
    }

    [Fact]
    public void GenerateLink_ReturnsAbsoluteUrlFromRouteData()
    {
        var expectedUrl = "https://example.gov.uk/WhatToChange/Index?windowId=123";
        var sut = CreateSut(expectedUrl, new NotifySettings());

        var result = sut.GenerateLink("WhatToChange", "Index", new { windowId = "123" }, "");

        Assert.Equal(expectedUrl, result);
    }

    [Fact]
    public void GenerateLink_WhenRouteUnresolvable_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(null, new NotifySettings());

        Assert.Throws<InvalidOperationException>(() =>
            sut.GenerateLink("InvalidController", "InvalidAction", null, "test-campaign"));
    }

    [Fact]
    public void GenerateLink_PreservesExistingQueryParameters()
    {
        var expectedUrl = "https://example.gov.uk/WhatToChange/Index?windowId=abc-123&existing=value";
        var sut = CreateSut(expectedUrl, new NotifySettings());

        var result = sut.GenerateLink("WhatToChange", "Index", new { windowId = "abc-123" }, "");

        Assert.Equal(expectedUrl, result);
    }

    [Fact]
    public void GenerateLink_AppendsUtmParameters()
    {
        var settings = new NotifySettings
        {
            UtmSource = "notify",
            UtmMedium = "email",
            UtmCampaigns = new Dictionary<string, string> { ["submission"] = "submission-notification" }
        };
        var sut = CreateSut("https://example.gov.uk/WhatToChange/Index?windowId=123", settings, "submission");

        var result = sut.GenerateLink("WhatToChange", "Index", new { windowId = "123" }, "submission");

        Assert.Contains("utm_source=notify", result);
        Assert.Contains("utm_medium=email", result);
        Assert.Contains("utm_campaign=submission-notification", result);
    }

    [Fact]
    public void GenerateLink_UtmOverridesExistingUtmParams()
    {
        var settings = new NotifySettings
        {
            UtmSource = "notify",
            UtmMedium = "email",
            UtmCampaigns = new Dictionary<string, string> { ["test"] = "new-campaign" }
        };
        var sut = CreateSut(
            "https://example.gov.uk/WhatToChange/Index?utm_source=old&utm_medium=old&utm_campaign=old",
            settings,
            "test");

        var result = sut.GenerateLink("WhatToChange", "Index", null, "test");

        Assert.Contains("utm_source=notify", result);
        Assert.Contains("utm_medium=email", result);
        Assert.Contains("utm_campaign=new-campaign", result);
        Assert.DoesNotContain("utm_source=old", result);
        Assert.DoesNotContain("utm_medium=old", result);
        Assert.DoesNotContain("utm_campaign=old", result);
    }

    [Fact]
    public void GenerateLink_MissingConfigOmitsUtmParameters()
    {
        var sut = CreateSut("https://example.gov.uk/WhatToChange/Index?windowId=123", new NotifySettings(), "");

        var result = sut.GenerateLink("WhatToChange", "Index", new { windowId = "123" }, "");

        Assert.DoesNotContain("utm_", result);
    }

    [Fact]
    public void GenerateLink_ConfigValuesReflectConfiguration()
    {
        var settings = new NotifySettings
        {
            UtmSource = "custom-source",
            UtmMedium = "custom-medium"
        };
        var sut = CreateSut("https://example.gov.uk/WhatToChange/Index", settings, "");

        var result = sut.GenerateLink("WhatToChange", "Index", null, "");

        Assert.Contains("utm_source=custom-source", result);
        Assert.Contains("utm_medium=custom-medium", result);
    }

    private EmailLinkGenerator CreateSut(string? returnUrl, NotifySettings settings, string? campaignName = null)
    {
        return new TestableEmailLinkGenerator(
            _httpContextAccessor,
            Options.Create(settings),
            _logger,
            returnUrl);
    }

    private sealed class TestableEmailLinkGenerator(
        IHttpContextAccessor httpContextAccessor,
        IOptions<NotifySettings> notifySettings,
        ILogger<EmailLinkGenerator> logger,
        string? returnUrl)
        : EmailLinkGenerator(
            Substitute.For<LinkGenerator>(),
            httpContextAccessor,
            notifySettings,
            logger)
    {
        protected override string? GetUriByActionCore(HttpContext httpContext, string action, string controller, object? routeValues)
        {
            return returnUrl;
        }
    }
}
