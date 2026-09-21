using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Egress;

// The concurrency rule of AB#294553 — one egress run per window and output type at a time, and
// never a second one after a successful transfer — is a database constraint, not a service
// check. These pin the constraint itself, so a race the service check misses is still refused.
[Collection(nameof(PostgresCollection))]
public sealed class EgressSchemaTests(PostgresFixture fixture)
{
    private static readonly Guid WindowId = Guid.Parse("A0000000-0000-0000-0000-00000000E601");

    private async Task EnsureWindowAsync()
    {
        await using var db = fixture.CreateContext();
        if (await db.CheckingWindows.AnyAsync(w => w.Id == WindowId)) return;
        db.CheckingWindows.Add(new CheckingWindow
        {
            Id = WindowId, Title = "Egress schema window", KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
        });
        await db.SaveChangesAsync();
    }

    private static EgressRun Run(EgressRunStatus status, bool active, EgressOutputType type) => new()
    {
        Id = Guid.NewGuid(), WindowId = WindowId, Status = status,
        StartedById = Guid.NewGuid(), StartedByName = "Ops One", StartedAtUtc = DateTime.UtcNow,
        Outputs =
        {
            new EgressRunOutput
            {
                Id = Guid.NewGuid(), WindowId = WindowId, OutputType = type, IsActive = active,
                RawRecordsJson = "[]", SourceRecordCount = 0
            }
        }
    };

    [Fact]
    public async Task A_second_active_output_for_the_same_window_and_type_is_refused_by_the_database()
    {
        await EnsureWindowAsync();
        var type = EgressOutputType.RemoveLearners;
        await using (var db = fixture.CreateContext())
        {
            await db.EgressRunOutputs.Where(o => o.WindowId == WindowId && o.OutputType == type).ExecuteDeleteAsync();
            db.EgressRuns.Add(Run(EgressRunStatus.Pulled, active: true, type));
            await db.SaveChangesAsync();
        }

        await using var second = fixture.CreateContext();
        second.EgressRuns.Add(Run(EgressRunStatus.Pulled, active: true, type));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23505", pg.SqlState);
        Assert.Equal("ix_egress_run_outputs_active_window_output", pg.ConstraintName);
    }

    [Fact]
    public async Task An_inactive_output_does_not_block_a_new_active_one()
    {
        await EnsureWindowAsync();
        var type = EgressOutputType.NewLearners;
        await using var db = fixture.CreateContext();
        await db.EgressRunOutputs.Where(o => o.WindowId == WindowId && o.OutputType == type).ExecuteDeleteAsync();
        db.EgressRuns.Add(Run(EgressRunStatus.PreprocessingFailed, active: false, type));
        db.EgressRuns.Add(Run(EgressRunStatus.Abandoned, active: false, type));
        db.EgressRuns.Add(Run(EgressRunStatus.Pulled, active: true, type));
        await db.SaveChangesAsync();   // no throw

        Assert.Equal(3, await db.EgressRunOutputs.CountAsync(o => o.WindowId == WindowId && o.OutputType == type));
    }

    [Fact]
    public async Task Learner_tables_and_the_request_laestab_column_exist()
    {
        await using var db = fixture.CreateContext();
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            select count(*) from information_schema.tables
            where table_name in ('egress_runs','egress_run_outputs','new_learners','remove_learners')
            """, conn);
        Assert.Equal(4L, (long)(await cmd.ExecuteScalarAsync())!);

        await using var col = new NpgsqlCommand(
            """select character_maximum_length from information_schema.columns where table_name = 'ChangeRequests' and column_name = 'OrganisationLaestab'""", conn);
        Assert.Equal(20, (int)(await col.ExecuteScalarAsync())!);
    }
}
