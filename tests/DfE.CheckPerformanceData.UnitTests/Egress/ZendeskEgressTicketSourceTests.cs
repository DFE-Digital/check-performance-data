using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The real source reads the "Decision status" custom field from tickets fetched by id in
// batches of 100 — Zendesk's show_many ceiling — and, for a final decision, the matching
// "Decision Reason" tagger field, resolved to its human-readable label from the field
// definitions (one ticket_fields.json call per pull). Production configures no field ids at
// all, so an unconfigured decision id must refuse loudly rather than return "every ticket
// unknown". The reason is the best-effort half of the read: a failed field-definition call
// drops the reasons, it never fails the decisions.
public sealed class ZendeskEgressTicketSourceTests
{
    private const long DecisionFieldId = 19056253670034;
    private const long ApprovedReasonFieldId = 19056477269010;
    private const long RejectedReasonFieldId = 19197936168210;
    private readonly IZendeskApi _api = Substitute.For<IZendeskApi>();

    private ZendeskEgressTicketSource Sut(
        long? fieldId = DecisionFieldId,
        long? approvedReasonId = ApprovedReasonFieldId,
        long? rejectedReasonId = RejectedReasonFieldId) => new(
        _api,
        Options.Create(new ZendeskTicketFieldSettings
        {
            DecisionStatusId = fieldId,
            DecisionReasonApprovedId = approvedReasonId,
            DecisionReasonRejectedId = rejectedReasonId
        }),
        // Polly's RetryStrategyOptions requires MaxRetryAttempts >= 1 (0 fails options validation);
        // 1 keeps retries minimal so a failing-API test still runs fast.
        Options.Create(new PollySettings { MaxRetryAttempts = 1, BaseDelayMilliseconds = 1, JitterMilliseconds = 0 }),
        Substitute.For<ILogger<ZendeskEgressTicketSource>>());

    private static Ticket TicketWith(long id, params (long FieldId, object? Value)[] fields) => new()
    {
        Id = id,
        CustomFields = fields.Select(f => new CustomField { Id = f.FieldId, Value = f.Value }).ToList()
    };

    private static void NoFieldDefinitions(IZendeskApi api) =>
        api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = [] });

    private static void FieldDefinitions(
        IZendeskApi api,
        params (long FieldId, (string Value, string Name)[] Options)[] fields)
    {
        api.GetTicketFields().Returns(new TicketFieldsResponse
        {
            TicketFields = fields.Select(f => new CustomFieldMetaData
            {
                Id = f.FieldId,
                CustomFieldOptions = f.Options.Select(o => new CustomFieldOption { Value = o.Value, Name = o.Name }).ToList()
            }).ToList()
        });
    }

    [Fact]
    public async Task Reads_the_decision_field_for_each_ticket_and_omits_tickets_zendesk_did_not_return()
    {
        // All four requested ids go into one call (batch size 100); Zendesk simply omits ticket 4
        // from its response, which is the "not returned by Zendesk" case this test is pinning.
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "auto_approved")), TicketWith(2, (DecisionFieldId, "Rejected")), TicketWith(3, (999, "x"))]
        });
        NoFieldDefinitions(_api);

        var result = await Sut().GetTicketDecisionsAsync([1, 2, 3, 4], CancellationToken.None);

        Assert.Equal("auto_approved", result[1].Decision);
        Assert.Equal("rejected", result[2].Decision);
        Assert.False(result.ContainsKey(3));   // no decision field on the ticket = unknown, not "approved"
        Assert.False(result.ContainsKey(4));   // not returned by Zendesk
    }

    [Fact]
    public async Task Batches_ids_one_hundred_at_a_time()
    {
        var ids = Enumerable.Range(1, 250).Select(i => (long)i).ToList();
        _api.ShowManyTickets(Arg.Any<string>()).Returns(ci =>
        {
            var body = ci.Arg<string>();
            var ticketIds = body.Split(',').Select(long.Parse).ToList();
            return new ListViewTicketsResponse
            {
                Tickets = ticketIds.Select(id => TicketWith(id, (DecisionFieldId, "approved"))).ToList()
            };
        });
        NoFieldDefinitions(_api);

        var result = await Sut().GetTicketDecisionsAsync(ids, CancellationToken.None);

        Assert.Equal(250, result.Count);
        await _api.Received(3).ShowManyTickets(Arg.Any<string>());
    }

    [Fact]
    public async Task No_ids_means_no_call()
    {
        var result = await Sut().GetTicketDecisionsAsync([], CancellationToken.None);
        Assert.Empty(result);
        await _api.DidNotReceive().ShowManyTickets(Arg.Any<string>());
        await _api.DidNotReceive().GetTicketFields();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task An_unconfigured_decision_field_refuses_to_pull(long? fieldId)
    {
        var ex = await Assert.ThrowsAsync<EgressTicketSourceException>(() => Sut(fieldId).GetTicketDecisionsAsync([1], CancellationToken.None));
        Assert.Contains("Decision status", ex.Message);
        await _api.DidNotReceive().ShowManyTickets(Arg.Any<string>());
        await _api.DidNotReceive().GetTicketFields();
    }

    [Fact]
    public async Task An_api_failure_surfaces_as_a_ticket_source_exception()
    {
        NoFieldDefinitions(_api);
        _api.ShowManyTickets(Arg.Any<string>()).Returns<Task<ListViewTicketsResponse>>(_ => throw new HttpRequestException("boom"));
        await Assert.ThrowsAsync<EgressTicketSourceException>(() => Sut().GetTicketDecisionsAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task Reads_the_reason_for_an_approved_decision_as_its_label()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "auto_approved"), (ApprovedReasonFieldId, "inclusion_criteria_met"))]
        });
        FieldDefinitions(_api, (ApprovedReasonFieldId, [("inclusion_criteria_met", "Inclusion criteria met")]));

        var result = await Sut().GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("auto_approved", result[1].Decision);
        Assert.Equal("Inclusion criteria met", result[1].Reason);
    }

    [Fact]
    public async Task Reads_the_reason_for_a_rejected_decision_from_the_rejected_field()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "rejected"), (RejectedReasonFieldId, "pupil_missing_in_education_criteria_not_met"))]
        });
        FieldDefinitions(_api, (RejectedReasonFieldId, [("pupil_missing_in_education_criteria_not_met", "Pupil missing in education: criteria not met")]));

        var result = await Sut().GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("rejected", result[1].Decision);
        Assert.Equal("Pupil missing in education: criteria not met", result[1].Reason);
    }

    [Fact]
    public async Task A_scrutiny_decision_carries_no_reason_even_if_a_reason_field_is_present()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "scrutiny"), (ApprovedReasonFieldId, "inclusion_criteria_met"))]
        });
        FieldDefinitions(_api, (ApprovedReasonFieldId, [("inclusion_criteria_met", "Inclusion criteria met")]));

        var result = await Sut().GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("scrutiny", result[1].Decision);
        Assert.Null(result[1].Reason);
    }

    [Fact]
    public async Task A_ticket_without_the_reason_field_has_a_null_reason()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "approved"))]
        });
        NoFieldDefinitions(_api);

        var result = await Sut().GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("approved", result[1].Decision);
        Assert.Null(result[1].Reason);
    }

    [Fact]
    public async Task A_reason_code_with_no_matching_option_has_a_null_reason()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "auto_approved"), (ApprovedReasonFieldId, "some_unknown_code"))]
        });
        FieldDefinitions(_api, (ApprovedReasonFieldId, [("inclusion_criteria_met", "Inclusion criteria met")]));

        var result = await Sut().GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("auto_approved", result[1].Decision);
        Assert.Null(result[1].Reason);
    }

    [Fact]
    public async Task Unset_reason_field_ids_still_return_the_decision_with_a_null_reason()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "auto_approved"), (ApprovedReasonFieldId, "inclusion_criteria_met"))]
        });

        var result = await Sut(approvedReasonId: null, rejectedReasonId: null).GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("auto_approved", result[1].Decision);
        Assert.Null(result[1].Reason);
        await _api.DidNotReceive().GetTicketFields();
    }

    [Fact]
    public async Task A_failed_field_definition_call_keeps_the_decisions_and_drops_the_reasons()
    {
        _api.ShowManyTickets(Arg.Any<string>()).Returns(new ListViewTicketsResponse
        {
            Tickets = [TicketWith(1, (DecisionFieldId, "auto_approved"), (ApprovedReasonFieldId, "inclusion_criteria_met"))]
        });
        _api.GetTicketFields().Returns(Task.FromException<TicketFieldsResponse>(new HttpRequestException("boom")));

        var result = await Sut().GetTicketDecisionsAsync([1], CancellationToken.None);

        Assert.Equal("auto_approved", result[1].Decision);
        Assert.Null(result[1].Reason);
    }
}
