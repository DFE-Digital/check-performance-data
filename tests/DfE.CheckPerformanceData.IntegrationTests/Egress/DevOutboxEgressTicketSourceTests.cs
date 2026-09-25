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
// opts into Egress:UseDevOutbox, could never pull.
//
// Deviation from the plan: the unit-test project has no in-memory-EF pattern and must not gain a
// MockQueryable dependency, so — per the plan's own fallback — this class lives here against real
// Postgres via PostgresFixture rather than in the unit-test project against a substituted context.
[Collection(nameof(PostgresCollection))]
public sealed class DevOutboxEgressTicketSourceTests(PostgresFixture fixture)
{
    private const long DecisionFieldId = 19056253670034;

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

        var result = await sut.GetDecisionStatusesAsync([10, 11, 12, 13, 14], CancellationToken.None);

        Assert.Equal("approved", result[10]);
        Assert.Equal("auto_approved", result[11]);
        Assert.Equal("auto_rejected", result[12]);
        Assert.Equal("scrutiny", result[13]);
        Assert.False(result.ContainsKey(14));
    }

    [Fact]
    public async Task Uses_the_well_known_dev_field_id_when_none_is_configured()
    {
        await SeedAsync(Ticket(20, "CPMD Requires Scrutiny: NotOnRoll (REF-E)", (DecisionFieldId, "rejected")));
        var sut = new DevOutboxEgressTicketSource(fixture.CreateContext(), Options.Create(new ZendeskTicketFieldSettings()));

        var result = await sut.GetDecisionStatusesAsync([20], CancellationToken.None);

        Assert.Equal("rejected", result[20]);
    }
}
