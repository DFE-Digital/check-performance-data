using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Audit;

[Collection(nameof(PostgresCollection))]
public sealed class AuditEntryActionLengthTests
{
    private readonly PostgresFixture _fixture;

    public AuditEntryActionLengthTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // The DLQ admin surface records audit actions longer than the original Insert/Update/Delete
    // vocabulary — e.g. "ViewFullPayload" when an admin reveals a redacted payload. The Action
    // column must hold them; a column too narrow overflows and 500s the full-payload view,
    // losing the forensic record the audit exists to keep.
    [Fact]
    public async Task FullPayloadViewAuditAction_Persists()
    {
        await using var context = _fixture.CreateContext();

        var entry = new AuditEntry
        {
            EntityType = "DlqMessage",
            EntityId = Guid.NewGuid().ToString(),
            Action = "ViewFullPayload",
            Timestamp = DateTime.UtcNow,
        };

        context.AuditEntries.Add(entry);
        await context.SaveChangesAsync();

        await using var verify = _fixture.CreateContext();
        var saved = await verify.AuditEntries.FirstOrDefaultAsync(a => a.Id == entry.Id);

        Assert.NotNull(saved);
        Assert.Equal("ViewFullPayload", saved!.Action);
    }
}
