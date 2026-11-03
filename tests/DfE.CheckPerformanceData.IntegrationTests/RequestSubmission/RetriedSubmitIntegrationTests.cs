using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

// #753: PortalDbContext is registered with EnableRetryOnFailure. When Postgres cancels the
// serializable submit transaction (40001), the execution strategy re-runs the whole delegate in
// RequestRepository.UpsertAsync. The first attempt's Add is still tracked as Added, so without a
// ChangeTracker.Clear() at the top of the delegate the retry saves two rows with one reference
// number, IX_ChangeRequests_ReferenceNumber raises 23505, and the school gets a 500.
//
// The retry is forced by an interceptor that throws a real 40001 on the first "ChangeRequests"
// insert, so the test is deterministic: no timing, no second writer.
[Collection(nameof(PostgresCollection))]
public sealed class RetriedSubmitIntegrationTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    // The premise of the whole chain. If a Npgsql upgrade ever stops treating 40001 as
    // transient, the retry (and this hazard) goes away and the forced-retry test below would
    // start failing for a different reason; this fact says why.
    [Fact]
    public void Npgsql_treats_a_serialization_failure_as_transient_and_a_unique_violation_as_not()
    {
        Assert.True(new PostgresException("m", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure).IsTransient);
        Assert.False(new PostgresException("m", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation).IsTransient);
    }

    [Fact]
    public async Task A_submit_that_is_retried_after_a_transient_fault_inserts_one_row_only()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var interceptor = new FailFirstChangeRequestInsertInterceptor();
        await using var context = _fixture.CreateRetryingContext(interceptor);
        var repository = new RequestRepository(context);

        var id = await repository.UpsertAsync(Data(windowId, "REF-RETRY-1", RequestStatus.Submitted, pupilId: Guid.NewGuid()));

        // The retry really happened: one insert failed, one succeeded.
        Assert.Equal(2, interceptor.ChangeRequestInserts);

        await using var verify = _fixture.CreateContext();
        var row = await verify.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-RETRY-1");
        Assert.Equal(id, row.Id);
        Assert.Equal(RequestStatus.Submitted, row.Status);

        // The failed attempt's audit row must not be saved alongside the real one either.
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(ChangeRequest) && a.EntityId == id.ToString() && a.Action == "Insert"));
        Assert.Equal(0, await verify.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(ChangeRequest) && a.Action == "Insert" && a.EntityId != id.ToString()));
    }

    private static ChangeRequestData Data(
        Guid windowId, string referenceNumber, RequestStatus status = RequestStatus.Submitted,
        long organisationUrn = 100000, Guid? pupilId = null, string? pupilUpn = "UPN1", Guid? submittedById = null) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = organisationUrn,
            PupilId = pupilId,
            PupilUpn = pupilUpn,
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
            SubmittedById = submittedById ?? Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = status,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove"
        };

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE; TRUNCATE ""AuditEntries"";";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedWindowAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }
}
