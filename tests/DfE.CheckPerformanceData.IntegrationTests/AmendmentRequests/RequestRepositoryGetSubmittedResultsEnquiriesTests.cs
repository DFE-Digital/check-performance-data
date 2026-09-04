using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.AmendmentRequests;

// AB#298325: the Issues tab lists the results enquiries a school has submitted. The Requests-tab
// query (GetSubmittedRequestsAsync) deliberately EXCLUDES enquiry rows; this is the complementary
// query. If the two ever overlap, a row appears on both tabs and the ticket's separation AC fails.
[Collection(nameof(PostgresCollection))]
public sealed class RequestRepositoryGetSubmittedResultsEnquiriesTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task ReturnsOnlyResultsEnquiryRows()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var repo = new RequestRepository(_fixture.CreateContext());
        await repo.UpsertAsync(Data(windowId, "REF-ENQ-1", RequestType.ResultsEnquiry, pupilId: Guid.NewGuid()));
        await repo.UpsertAsync(Data(windowId, "REF-AMD-1", RequestType.Amendment, pupilId: Guid.NewGuid()));
        await repo.UpsertAsync(Data(windowId, "REF-CC-1", RequestType.ConfirmCorrect, pupilId: Guid.NewGuid()));

        var result = await new RequestRepository(_fixture.CreateContext())
            .GetSubmittedResultsEnquiriesAsync(windowId, 100000);

        var row = Assert.Single(result);
        Assert.Equal("REF-ENQ-1", row.ReferenceNumber);
        Assert.Equal(RequestType.ResultsEnquiry, row.RequestType);
    }

    [Fact]
    public async Task ScopesByWindowAndOrganisation()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var otherWindowId = await SeedWindowAsync();
        var repo = new RequestRepository(_fixture.CreateContext());
        await repo.UpsertAsync(Data(windowId, "REF-ENQ-MINE", RequestType.ResultsEnquiry, pupilId: Guid.NewGuid()));
        await repo.UpsertAsync(Data(otherWindowId, "REF-ENQ-OTHER-WINDOW", RequestType.ResultsEnquiry, pupilId: Guid.NewGuid()));
        // A different school's enquiry in the same window must never leak into this school's list.
        await repo.UpsertAsync(Data(windowId, "REF-ENQ-OTHER-ORG", RequestType.ResultsEnquiry, pupilId: Guid.NewGuid(), organisationUrn: 999999));

        var result = await new RequestRepository(_fixture.CreateContext())
            .GetSubmittedResultsEnquiriesAsync(windowId, 100000);

        Assert.Equal(["REF-ENQ-MINE"], result.Select(r => r.ReferenceNumber));
    }

    [Fact]
    public async Task OrdersNewestFirst()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var repo = new RequestRepository(_fixture.CreateContext());
        await repo.UpsertAsync(Data(windowId, "REF-ENQ-OLD", RequestType.ResultsEnquiry, pupilId: Guid.NewGuid(), timestamp: DateTime.UtcNow.AddDays(-2)));
        await repo.UpsertAsync(Data(windowId, "REF-ENQ-NEW", RequestType.ResultsEnquiry, pupilId: Guid.NewGuid(), timestamp: DateTime.UtcNow));

        var result = await new RequestRepository(_fixture.CreateContext())
            .GetSubmittedResultsEnquiriesAsync(windowId, 100000);

        Assert.Equal(["REF-ENQ-NEW", "REF-ENQ-OLD"], result.Select(r => r.ReferenceNumber));
    }

    private static ChangeRequestData Data(
        Guid windowId, string referenceNumber, RequestType requestType, Guid? pupilId = null,
        long organisationUrn = 100000, DateTime? timestamp = null) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = organisationUrn,
            PupilId = pupilId,
            PupilUpn = pupilId is null ? null : "UPN-" + pupilId,
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = timestamp ?? DateTime.UtcNow,
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = requestType,
            RequestTypeDescription = requestType.ToString()
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
