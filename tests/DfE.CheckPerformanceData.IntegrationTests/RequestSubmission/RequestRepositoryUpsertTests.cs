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

    private static ChangeRequestData Data(
        Guid windowId, string referenceNumber, RequestStatus status = RequestStatus.SubmittedUnCommitted) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = 100000,
            PupilUpn = "UPN1",
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = status,
            RequestType = "Remove"
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
