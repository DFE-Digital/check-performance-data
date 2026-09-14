using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The real source reads ONE custom field ("Decision status") from tickets fetched by id in
// batches of 100 — Zendesk's show_many ceiling. Production configures no field ids at all, so an
// unconfigured id must refuse loudly rather than return "every ticket unknown".
public sealed class ZendeskEgressTicketSourceTests
{
    private const long DecisionFieldId = 19056253670034;
    private readonly IZendeskApi _api = Substitute.For<IZendeskApi>();

    private ZendeskEgressTicketSource Sut(long? fieldId = DecisionFieldId) => new(
        _api,
        Options.Create(new ZendeskTicketFieldSettings { DecisionStatusId = fieldId }),
        // Polly's RetryStrategyOptions requires MaxRetryAttempts >= 1 (0 fails options validation);
        // 1 keeps retries minimal so a failing-API test still runs fast.
        Options.Create(new PollySettings { MaxRetryAttempts = 1, BaseDelayMilliseconds = 1, JitterMilliseconds = 0 }),
        Substitute.For<ILogger<ZendeskEgressTicketSource>>());

    private static Ticket TicketWith(long id, params (long FieldId, object? Value)[] fields) => new()
    {
        Id = id,
        CustomFields = fields.Select(f => new CustomField { Id = f.FieldId, Value = f.Value }).ToList()
    };

    [Fact]
    public async Task Reads_the_decision_field_for_each_ticket_and_omits_tickets_zendesk_did_not_return()
    {
        // All four requested ids go into one call (batch size 100); Zendesk simply omits ticket 4
        // from its response, which is the "not returned by Zendesk" case this test is pinning.
        _api.ShowManyTickets("1,2,3,4").Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "auto_approved")), TicketWith(2, (DecisionFieldId, "Rejected")), TicketWith(3, (999, "x"))]
        });

        var result = await Sut().GetDecisionStatusesAsync([1, 2, 3, 4], CancellationToken.None);

        Assert.Equal("auto_approved", result[1]);
        Assert.Equal("rejected", result[2]);
        Assert.False(result.ContainsKey(3));   // no decision field on the ticket = unknown, not "approved"
        Assert.False(result.ContainsKey(4));   // not returned by Zendesk
    }

    [Fact]
    public async Task Batches_ids_one_hundred_at_a_time()
    {
        var ids = Enumerable.Range(1, 250).Select(i => (long)i).ToList();
        _api.ShowManyTickets(Arg.Any<string>()).Returns(ci => new ListViewTicketsResponse
        {
            Tickets = ci.Arg<string>().Split(',').Select(s => TicketWith(long.Parse(s), (DecisionFieldId, "approved"))).ToList()
        });

        var result = await Sut().GetDecisionStatusesAsync(ids, CancellationToken.None);

        Assert.Equal(250, result.Count);
        await _api.Received(3).ShowManyTickets(Arg.Any<string>());
        await _api.Received(1).ShowManyTickets(Arg.Is<string>(s => s.Split(',').Length == 50));
    }

    [Fact]
    public async Task No_ids_means_no_call()
    {
        var result = await Sut().GetDecisionStatusesAsync([], CancellationToken.None);
        Assert.Empty(result);
        await _api.DidNotReceive().ShowManyTickets(Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task An_unconfigured_decision_field_refuses_to_pull(long? fieldId)
    {
        var ex = await Assert.ThrowsAsync<EgressTicketSourceException>(() => Sut(fieldId).GetDecisionStatusesAsync([1], CancellationToken.None));
        Assert.Contains("Decision status", ex.Message);
        await _api.DidNotReceive().ShowManyTickets(Arg.Any<string>());
    }

    [Fact]
    public async Task An_api_failure_surfaces_as_a_ticket_source_exception()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns<Task<ListViewTicketsResponse>>(_ => throw new HttpRequestException("boom"));
        await Assert.ThrowsAsync<EgressTicketSourceException>(() => Sut().GetDecisionStatusesAsync([1], CancellationToken.None));
    }
}
