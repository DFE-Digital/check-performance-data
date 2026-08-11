namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

public sealed class ClientEventsControllerTests
{
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();

    private ClientEventsController NewSut(string? referer = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("host");
        if (referer is not null)
        {
            httpContext.Request.Headers.Referer = referer;
        }

        return new ClientEventsController(_analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private AnalyticsEvent? Captured()
    {
        var calls = _analytics.ReceivedCalls().ToList();
        return calls.Count == 0 ? null : (AnalyticsEvent?)calls[0].GetArguments()[0];
    }

    [Fact]
    public async Task Help_details_expanded_returns_204_and_emits_event_with_referer_path_only()
    {
        var sut = NewSut("https://host/Journey/x?fromSummary=true");

        var result = await sut.Post(new ClientEventsController.ClientEventRequest
        {
            EventName = "help_details_expanded",
            ExpandText = "How can I find a DfE number?",
        });

        Assert.IsType<NoContentResult>(result);
        var e = Assert.IsType<HelpDetailsExpandedEvent>(Captured());
        Assert.Equal("How can I find a DfE number?", e.ExpandText);
        Assert.Equal("/Journey/x", e.PagePath); // query stripped
    }

    [Fact]
    public async Task Expand_text_is_truncated_to_100_chars()
    {
        var sut = NewSut();

        await sut.Post(new ClientEventsController.ClientEventRequest
        {
            EventName = "help_details_expanded",
            ExpandText = new string('a', 250),
        });

        var e = Assert.IsType<HelpDetailsExpandedEvent>(Captured());
        Assert.Equal(100, e.ExpandText!.Length);
    }

    [Fact]
    public async Task Gias_hostname_maps_to_gias_destination()
    {
        var sut = NewSut();

        var result = await sut.Post(new ClientEventsController.ClientEventRequest
        {
            EventName = "external_link_clicked",
            Destination = "get-information-schools.service.gov.uk",
        });

        Assert.IsType<NoContentResult>(result);
        var e = Assert.IsType<ExternalLinkClickedEvent>(Captured());
        Assert.Equal("gias", e.Destination);
    }

    [Fact]
    public async Task Other_hostnames_pass_through_lowercased()
    {
        var sut = NewSut();

        await sut.Post(new ClientEventsController.ClientEventRequest
        {
            EventName = "external_link_clicked",
            Destination = "Example.ORG",
        });

        var e = Assert.IsType<ExternalLinkClickedEvent>(Captured());
        Assert.Equal("example.org", e.Destination);
    }

    [Fact]
    public async Task Evidence_file_selected_returns_204_and_emits_event()
    {
        var sut = NewSut("https://host/Journey/x/page/evidence");

        var result = await sut.Post(new ClientEventsController.ClientEventRequest
        {
            EventName = "evidence_file_selected",
        });

        Assert.IsType<NoContentResult>(result);
        var e = Assert.IsType<EvidenceFileSelectedEvent>(Captured());
        Assert.Equal("/Journey/x/page/evidence", e.PagePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not_an_allowed_event")]
    [InlineData("request_submitted")] // server-only events must not be client-triggerable
    public async Task Unknown_or_disallowed_event_names_return_400_and_emit_nothing(string? eventName)
    {
        var sut = NewSut();

        var result = await sut.Post(new ClientEventsController.ClientEventRequest { EventName = eventName });

        Assert.IsType<BadRequestResult>(result);
        Assert.Null(Captured());
    }

    [Fact]
    public async Task Missing_referer_yields_null_page_path()
    {
        var sut = NewSut(referer: null);

        await sut.Post(new ClientEventsController.ClientEventRequest { EventName = "evidence_file_selected" });

        var e = Assert.IsType<EvidenceFileSelectedEvent>(Captured());
        Assert.Null(e.PagePath);
    }

    [Fact]
    public async Task Cross_origin_referer_yields_null_page_path()
    {
        // AB#286387 whole-branch review Finding 2: only same-origin referers are trusted
        // as a page_path source.
        var sut = NewSut("https://evil.example/Journey/x");

        await sut.Post(new ClientEventsController.ClientEventRequest { EventName = "evidence_file_selected" });

        var e = Assert.IsType<EvidenceFileSelectedEvent>(Captured());
        Assert.Null(e.PagePath);
    }

    [Fact]
    public async Task Long_same_origin_referer_path_is_truncated()
    {
        var longPath = "/" + new string('a', 250);
        var sut = NewSut($"https://host{longPath}");

        await sut.Post(new ClientEventsController.ClientEventRequest { EventName = "evidence_file_selected" });

        var e = Assert.IsType<EvidenceFileSelectedEvent>(Captured());
        Assert.Equal(100, e.PagePath!.Length);
        Assert.Equal(longPath[..100], e.PagePath);
    }
}
