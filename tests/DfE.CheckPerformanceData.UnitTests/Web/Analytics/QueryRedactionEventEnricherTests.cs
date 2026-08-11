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
}
