using Dfe.Analytics.AspNetCore;
using Dfe.Analytics.Events;
using DfE.CheckPerformanceData.Web.Analytics;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Analytics;

public sealed class QueryRedactionEventEnricherTests
{
    private static async Task<Event> EnrichAsync(IDictionary<string, string[]> requestQuery)
    {
        var httpContext = new DefaultHttpContext();
        var @event = new Event
        {
            EventType = "web_request",
            Environment = "test",
            RequestQuery = requestQuery,
        };

        await new QueryRedactionEventEnricher()
            .EnrichEventAsync(new EnrichWebRequestEventContext(@event, httpContext));

        return @event;
    }

    [Fact]
    public async Task Masks_denylisted_param_values_in_request_query_dictionary()
    {
        // Event.RequestQuery is populated by the library as IDictionary<string, string[]>
        // (see AspNetCoreEventSender.PopulateEventFromRequest), not a raw query string.
        var ev = await EnrichAsync(new Dictionary<string, string[]>
        {
            ["includedSearch"] = ["John Smith"],
            ["activeTab"] = ["included"],
        });

        Assert.Equal([QueryRedaction.Mask], ev.RequestQuery["includedSearch"]);
        Assert.Equal(["included"], ev.RequestQuery["activeTab"]);
    }

    [Fact]
    public async Task Leaves_help_search_query_param_untouched()
    {
        // R21: the site/help search term ("q") must NOT be redacted.
        var ev = await EnrichAsync(new Dictionary<string, string[]>
        {
            ["q"] = ["pupil premium"],
        });

        Assert.Equal(["pupil premium"], ev.RequestQuery["q"]);
    }

    [Fact]
    public async Task Keeps_empty_denylisted_values_empty()
    {
        var ev = await EnrichAsync(new Dictionary<string, string[]>
        {
            ["includedSearch"] = [""],
        });

        Assert.Equal([""], ev.RequestQuery["includedSearch"]);
    }

    [Fact]
    public async Task Does_not_throw_when_request_query_is_null()
    {
        var httpContext = new DefaultHttpContext();
        var @event = new Event { EventType = "web_request", Environment = "test" };

        await new QueryRedactionEventEnricher()
            .EnrichEventAsync(new EnrichWebRequestEventContext(@event, httpContext));

        Assert.Null(@event.RequestQuery);
    }

    private static async Task<Event> EnrichRefererAsync(string? referer)
    {
        var httpContext = new DefaultHttpContext();
        var @event = new Event
        {
            EventType = "web_request",
            Environment = "test",
            RequestReferer = referer,
        };

        await new QueryRedactionEventEnricher()
            .EnrichEventAsync(new EnrichWebRequestEventContext(@event, httpContext));

        return @event;
    }

    [Fact]
    public async Task Masks_denylisted_param_values_in_request_referer()
    {
        // AB#286387 whole-branch review Finding 1: Event.RequestReferer is a separate
        // raw-string field (the Referer header verbatim) that must be scrubbed
        // independently of RequestQuery.
        var ev = await EnrichRefererAsync(
            "https://host/CheckYourPupilData/abc?includedSearch=John+Smith&activeTab=included");

        Assert.Equal(
            "https://host/CheckYourPupilData/abc?includedSearch=%5Bredacted%5D&activeTab=included",
            ev.RequestReferer);
    }

    [Fact]
    public async Task Leaves_request_referer_without_denylisted_params_unchanged()
    {
        var ev = await EnrichRefererAsync("https://host/CheckYourPupilData/abc?activeTab=included");

        Assert.Equal("https://host/CheckYourPupilData/abc?activeTab=included", ev.RequestReferer);
    }

    [Fact]
    public async Task Leaves_request_referer_without_query_string_unchanged()
    {
        var ev = await EnrichRefererAsync("https://host/CheckYourPupilData/abc");

        Assert.Equal("https://host/CheckYourPupilData/abc", ev.RequestReferer);
    }

    [Fact]
    public async Task Leaves_null_request_referer_unchanged()
    {
        var ev = await EnrichRefererAsync(null);

        Assert.Null(ev.RequestReferer);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/only?includedSearch=John")]
    public async Task Leaves_malformed_or_non_absolute_request_referer_unchanged_without_throwing(string referer)
    {
        var ev = await EnrichRefererAsync(referer);

        Assert.Equal(referer, ev.RequestReferer);
    }
}
