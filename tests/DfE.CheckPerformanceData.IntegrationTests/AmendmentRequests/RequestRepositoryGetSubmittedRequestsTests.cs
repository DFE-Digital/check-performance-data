using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.AmendmentRequests;

// AB#296648: a results enquiry is stored as an ordinary SubmittedUnCommitted ChangeRequests row,
// but the Amendment Requests "submitted" tab is built for amendments — Delete quietly withdraws
// an enquiry via an unhandled-type fallback, and View renders a broken details page. How this
// screen should present a results enquiry is not yet designed, so enquiry rows are hidden here
// for now; they remain in ChangeRequests and reach support via the separate Zendesk story.
[Collection(nameof(PostgresCollection))]
public sealed class RequestRepositoryGetSubmittedRequestsTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task GetSubmittedRequestsAsync_ExcludesResultsEnquiryRows()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-ENQ-1", RequestType.ResultsEnquiry));

        var result = await new RequestRepository(_fixture.CreateContext())
            .GetSubmittedRequestsAsync(windowId, 100000);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubmittedRequestsAsync_StillReturnsAmendmentAndConfirmCorrectRows()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-AMD-1", RequestType.Amendment, pupilId: Guid.NewGuid()));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CC-1", RequestType.ConfirmCorrect, pupilId: Guid.NewGuid()));

        var result = await new RequestRepository(_fixture.CreateContext())
            .GetSubmittedRequestsAsync(windowId, 100000);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.ReferenceNumber == "REF-AMD-1" && r.RequestType == RequestType.Amendment);
        Assert.Contains(result, r => r.ReferenceNumber == "REF-CC-1" && r.RequestType == RequestType.ConfirmCorrect);
    }

    private static ChangeRequestData Data(
        Guid windowId, string referenceNumber, RequestType requestType, Guid? pupilId = null) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = 100000,
            PupilId = pupilId,
            PupilUpn = pupilId is null ? null : "UPN-" + pupilId,
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
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
