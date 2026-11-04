using System.Text.Json;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.Egress;

// The fake reads the worker's dev outbox: the decision custom field when the ticket carried one,
// else the subject prefix the ticket builder always writes. Without this the dev/E2E stack, which
// opts into Egress:UseDevOutbox, could never pull. The dev seeder writes the reason label text
// straight into the matching reason field value, so the fake returns it as-is — no option lookup.
//
// Deviation from the plan: the unit-test project has no in-memory-EF pattern and must not gain a
// MockQueryable dependency, so — per the plan's own fallback — this class lives here against real
// Postgres via PostgresFixture rather than in the unit-test project against a substituted context.
[Collection(nameof(PostgresCollection))]
public sealed class DevOutboxEgressTicketSourceTests(PostgresFixture fixture)
{
    private const long DecisionFieldId = 19056253670034;
    private const long ApprovedReasonFieldId = 19056477269010;
    private const long RejectedReasonFieldId = 19197936168210;

    private static DevZendeskTicket Ticket(long id, string subject, params (long FieldId, object? Value)[] fields) => new()
    {
        Id = Guid.NewGuid(), TicketId = id, Subject = subject, CreatedAtUtc = DateTime.UtcNow,
        RawJson = JsonSerializer.Serialize(new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto { Subject = subject, CustomFields = fields.Select(f => new CustomFieldDto { Id = f.FieldId, Value = f.Value }).ToList() }
        })
    };

    private async Task SeedAsync(params DevZendeskTicket[] rows)
    {
        await using var db = fixture.CreateContext();
        var ids = rows.Select(r => r.TicketId).ToList();
        await db.DevZendeskTickets.Where(t => ids.Contains(t.TicketId)).ExecuteDeleteAsync();
        db.DevZendeskTickets.AddRange(rows);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Prefers_the_decision_field_and_falls_back_to_the_subject()
    {
        await SeedAsync(
            Ticket(10, "CPMD Requires Scrutiny: NotOnRoll (REF-A)", (DecisionFieldId, "approved")),
            Ticket(11, "CPMD Auto-Approved: Deceased (REF-B)"),
            Ticket(12, "CPMD Auto-Rejected: Inclusion (REF-C)"),
            Ticket(13, "CPMD Requires Scrutiny: Other (REF-D)"));
        var sut = new DevOutboxEgressTicketSource(fixture.CreateContext(), Options.Create(new ZendeskTicketFieldSettings { DecisionStatusId = DecisionFieldId }));

        var result = await sut.GetTicketDecisionsAsync([10, 11, 12, 13, 14], CancellationToken.None);

        Assert.Equal("approved", result[10].Decision);
        Assert.Equal("auto_approved", result[11].Decision);
        Assert.Equal("auto_rejected", result[12].Decision);
        Assert.Equal("scrutiny", result[13].Decision);
        Assert.False(result.ContainsKey(14));
    }

    [Fact]
    public async Task Uses_the_well_known_dev_field_id_when_none_is_configured()
    {
        await SeedAsync(Ticket(20, "CPMD Requires Scrutiny: NotOnRoll (REF-E)", (DecisionFieldId, "rejected")));
        var sut = new DevOutboxEgressTicketSource(fixture.CreateContext(), Options.Create(new ZendeskTicketFieldSettings()));

        var result = await sut.GetTicketDecisionsAsync([20], CancellationToken.None);

        Assert.Equal("rejected", result[20].Decision);
    }

    [Fact]
    public async Task Reads_the_reason_field_matching_the_decision()
    {
        await SeedAsync(
            Ticket(30, "CPMD Requires Scrutiny: NotOnRoll (REF-F)", (DecisionFieldId, "approved"), (ApprovedReasonFieldId, "Inclusion criteria met")),
            Ticket(31, "CPMD Auto-Rejected: Inclusion (REF-G)", (DecisionFieldId, "auto_rejected"), (RejectedReasonFieldId, "Inclusion criteria not met")));
        var sut = new DevOutboxEgressTicketSource(fixture.CreateContext(), Options.Create(new ZendeskTicketFieldSettings { DecisionStatusId = DecisionFieldId }));

        var result = await sut.GetTicketDecisionsAsync([30, 31], CancellationToken.None);

        Assert.Equal("Inclusion criteria met", result[30].Reason);
        Assert.Equal("Inclusion criteria not met", result[31].Reason);
    }

    [Fact]
    public async Task A_scrutiny_decision_or_a_missing_reason_field_gives_a_null_reason()
    {
        await SeedAsync(
            Ticket(40, "CPMD Requires Scrutiny: Other (REF-H)", (DecisionFieldId, "scrutiny"), (ApprovedReasonFieldId, "Inclusion criteria met")),
            Ticket(41, "CPMD Requires Scrutiny: NotOnRoll (REF-I)", (DecisionFieldId, "approved")));
        var sut = new DevOutboxEgressTicketSource(fixture.CreateContext(), Options.Create(new ZendeskTicketFieldSettings { DecisionStatusId = DecisionFieldId }));

        var result = await sut.GetTicketDecisionsAsync([40, 41], CancellationToken.None);

        Assert.Null(result[40].Reason);
        Assert.Null(result[41].Reason);
    }
}
