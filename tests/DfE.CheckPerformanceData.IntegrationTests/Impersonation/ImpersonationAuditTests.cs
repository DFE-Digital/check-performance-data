using System.Text.Json;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Impersonation;

[Collection(nameof(PostgresCollection))]
public sealed class ImpersonationAuditTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("Start", "Success")]
    [InlineData("Exit", "Success")]
    [InlineData("BlockedChange", "Failed")]
    public async Task Events_persist_original_actor_target_timestamp_and_minimal_outcome(string action, string outcome)
    {
        var actor = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow;
        await using var db = fixture.CreateContext();
        await new ImpersonationAuditWriter(db).RecordAsync(new(actor, "1234567", "100001", action, timestamp), default);
        var row = await db.AuditEntries.AsNoTracking().SingleAsync(r => r.EntityType == "Impersonation" && r.UserId == actor);
        Assert.Equal(action, row.Action);
        Assert.InRange(Math.Abs(timestamp.Ticks - row.Timestamp.Ticks), 0L, 9L);
        Assert.Equal("LAESTAB 1234567, URN 100001", row.EntityId);
        using var payload = JsonDocument.Parse(row.NewValues!);
        Assert.Equal(3, payload.RootElement.EnumerateObject().Count());
        Assert.Equal("1234567", payload.RootElement.GetProperty("Laestab").GetString());
        Assert.Equal("100001", payload.RootElement.GetProperty("Urn").GetString());
        Assert.Equal(outcome, payload.RootElement.GetProperty("Outcome").GetString());
        Assert.Null(row.OldValues);
    }
}
