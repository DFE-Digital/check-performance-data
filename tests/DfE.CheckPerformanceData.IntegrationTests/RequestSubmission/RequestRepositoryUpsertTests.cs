using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

[Collection(nameof(PostgresCollection))]
public sealed class RequestRepositoryUpsertTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task Upsert_Insert_ReturnsIdOfNewRow()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();

        var id = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-INS-1"));

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-INS-1");
        Assert.Equal(row.Id, id);
    }

    [Fact]
    public async Task Upsert_Update_ReturnsIdOfExistingRow_AndDoesNotInsert()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var firstId = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-UPD-1", RequestStatus.InProgress));

        var secondId = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-UPD-1", RequestStatus.SubmittedUnCommitted));

        Assert.Equal(firstId, secondId);
        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-UPD-1");
        Assert.Equal(RequestStatus.SubmittedUnCommitted, row.Status);
    }

    [Fact]
    public async Task Withdraw_SetsStatusToWithdrawn_ScopedByOrg()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-WD-1", RequestStatus.SubmittedUnCommitted));
        // A different org's row with the same reference must be left untouched.
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-WD-OTHER", RequestStatus.SubmittedUnCommitted, organisationUrn: 999999));

        await new RequestRepository(_fixture.CreateContext())
            .WithdrawAsync(windowId, 100000, "REF-WD-1");

        await using var ctx = _fixture.CreateContext();
        var withdrawn = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-WD-1");
        Assert.Equal(RequestStatus.Withdrawn, withdrawn.Status);
        var other = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-WD-OTHER");
        Assert.Equal(RequestStatus.SubmittedUnCommitted, other.Status);
    }

    [Fact]
    public async Task Withdraw_DoesNotAffectAnotherOrgsRowWithSameReference()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-WD-2", RequestStatus.SubmittedUnCommitted));

        // Withdraw scoped to a different org should match nothing.
        await new RequestRepository(_fixture.CreateContext())
            .WithdrawAsync(windowId, 999999, "REF-WD-2");

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-WD-2");
        Assert.Equal(RequestStatus.SubmittedUnCommitted, row.Status);
    }

    [Fact]
    public async Task Delete_RemovesRow_ScopedByOrg()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-DEL-1", RequestStatus.InProgress));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-DEL-OTHER", RequestStatus.InProgress, organisationUrn: 999999));

        await new RequestRepository(_fixture.CreateContext())
            .DeleteAsync(windowId, 100000, "REF-DEL-1");

        await using var ctx = _fixture.CreateContext();
        Assert.False(await ctx.ChangeRequests.AnyAsync(r => r.ReferenceNumber == "REF-DEL-1"));
        Assert.True(await ctx.ChangeRequests.AnyAsync(r => r.ReferenceNumber == "REF-DEL-OTHER"));
    }

    [Fact]
    public async Task Delete_DoesNotRemoveAnotherOrgsRowWithSameReference()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-DEL-2", RequestStatus.InProgress));

        await new RequestRepository(_fixture.CreateContext())
            .DeleteAsync(windowId, 999999, "REF-DEL-2");

        await using var ctx = _fixture.CreateContext();
        Assert.True(await ctx.ChangeRequests.AnyAsync(r => r.ReferenceNumber == "REF-DEL-2"));
    }

    [Fact]
    public async Task HasConflictingRequest_TrueForSamePupilId_InSameWindowAndOrg()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONF-1", pupilId: pupilId));

        var conflict = await new RequestRepository(_fixture.CreateContext())
            .HasConflictingRequestAsync(windowId, pupilId, 100000, "REF-CONF-OTHER");

        Assert.True(conflict);
    }

    [Fact]
    public async Task HasConflictingRequest_FalseForDifferentPupilId_EvenWhenUpnMatches()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        // Two different pupils that share a (blank) UPN. A request for one must not conflict the other.
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONF-2", pupilId: Guid.NewGuid(), pupilUpn: ""));

        var conflict = await new RequestRepository(_fixture.CreateContext())
            .HasConflictingRequestAsync(windowId, Guid.NewGuid(), 100000, "REF-CONF-3");

        Assert.False(conflict);
    }

    [Fact]
    public async Task HasConflictingRequest_FalseForSamePupilButSameReference()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONF-4", pupilId: pupilId));

        // Re-submitting the same reference (the current draft) is not a conflict with itself.
        var conflict = await new RequestRepository(_fixture.CreateContext())
            .HasConflictingRequestAsync(windowId, pupilId, 100000, "REF-CONF-4");

        Assert.False(conflict);
    }

    [Fact]
    public async Task HasConflictingRequest_FalseWhenExistingRequestWithdrawn()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONF-5", RequestStatus.Withdrawn, pupilId: pupilId));

        var conflict = await new RequestRepository(_fixture.CreateContext())
            .HasConflictingRequestAsync(windowId, pupilId, 100000, "REF-CONF-6");

        Assert.False(conflict);
    }

    private static ChangeRequestData Data(
        Guid windowId, string referenceNumber, RequestStatus status = RequestStatus.SubmittedUnCommitted,
        long organisationUrn = 100000, Guid? pupilId = null, string? pupilUpn = "UPN1") =>
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
            SubmittedById = Guid.NewGuid(),
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
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
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
