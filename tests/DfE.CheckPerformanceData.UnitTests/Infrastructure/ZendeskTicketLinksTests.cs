using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Configuration;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// The admin requests page links each Zendesk id to its ticket (#780). The link goes to the
// instance that the environment sends tickets to, and only when the ticket is real.
public sealed class ZendeskTicketLinksTests
{
    private static ZendeskTicketLinks Links(string subdomain, string useFake = "false") =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Zendesk:UseFake"] = useFake,
                ["ZendeskSettings:Subdomain"] = subdomain,
                ["ZendeskSettings:Domain"] = "zendesk"
            })
            .Build());

    [Fact]
    public void TicketUrl_UsesTheProductionInstance()
    {
        Assert.Equal("https://esfa.zendesk.com/agent/tickets/91568", Links("esfa").TicketUrl("91568"));
    }

    [Fact]
    public void TicketUrl_UsesThePreprodInstance()
    {
        Assert.Equal(
            "https://esfa-preprod.zendesk.com/agent/tickets/91568",
            Links("esfa-preprod").TicketUrl("91568"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-ticket")]
    [InlineData("0")]
    public void TicketUrl_IsNull_WhenTheRequestHasNoTicketId(string? crmId)
    {
        Assert.Null(Links("esfa").TicketUrl(crmId));
    }

    [Fact]
    public void TicketUrl_IsNull_WhenTheFakeClientIsInUse()
    {
        // The fake client's ids are numbers, but no Zendesk has them.
        Assert.Null(Links("esfa-preprod", useFake: "true").TicketUrl("91568"));
    }

    [Fact]
    public void TicketUrl_IsNull_WhenUseFakeIsNotSet()
    {
        // The worker uses the fake client when the setting is missing, so the link does too.
        var links = new ZendeskTicketLinks(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ZendeskSettings:Subdomain"] = "esfa",
                ["ZendeskSettings:Domain"] = "zendesk"
            })
            .Build());

        Assert.Null(links.TicketUrl("91568"));
    }

    [Fact]
    public void TicketUrl_IsNull_WhenTheHostIsNotSet()
    {
        Assert.Null(Links("").TicketUrl("91568"));
    }
}
