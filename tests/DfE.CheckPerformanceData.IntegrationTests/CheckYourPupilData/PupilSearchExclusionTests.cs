using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

[Collection(nameof(PostgresCollection))]
public sealed class PupilSearchExclusionTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";
    private static readonly long TestUrnLong = long.Parse(TestUrn);
    private const int IncludedPincl = 401;

    private CheckYourPupilDataRepository CreateRepo(IPupilDataBlobClient blobClient)
        => new(fixture.CreateContext(), blobClient, new MemoryCache(new MemoryCacheOptions()));

    private async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE ""ChangeRequests"" CASCADE;
            TRUNCATE ""CheckingWindows"" RESTART IDENTITY CASCADE;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    // PostgreSQL 'timestamp without time zone' columns require DateTimeKind.Unspecified.
    private static DateTime Unspecified(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    private static CheckingWindow NewWindow(Guid id) => new()
    {
        Id = id,
        Title = "Test Window",
        StartDate = Unspecified(DateTime.UtcNow.AddDays(-7)),
        EndDate = Unspecified(DateTime.UtcNow.AddDays(7)),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
    };

    private static PupilRecord NewPupil(Guid windowId, string surname, string upn, int pincl = IncludedPincl) => new()
    {
        Id = Guid.NewGuid(),
        CheckingWindowId = windowId,
        Urn = TestUrn,
        Laestab = TestLaestab,
        Surname = surname,
        Firstname = "Test",
        Sex = "M",
        DateOfBirth = "01/01/2000",
        Age = 16,
        FirstLanguage = "English",
        Pincl = pincl,
        NewMobile = false,
        ActualYearGroup = "11",
        Ethnicity = "A1",
        SenF = "N",
        EntryDate = DateTime.UtcNow.AddYears(-2),
        Cypmd_Id = Guid.NewGuid().ToString(),
        MatchRef = 1,
        Upn = upn,
    };

    private static ChangeRequest NewChangeRequest(Guid windowId, long orgUrn, string upn, RequestStatus status = RequestStatus.SubmittedUnCommitted) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = windowId,
        OrganisationUrn = orgUrn,
        PupilUpn = upn,
        PupilFirstname = "Test",
        PupilSurname = "Pupil",
        Submitted = Unspecified(DateTime.UtcNow),
        SubmittedById = Guid.NewGuid(),
        SubmittedByName = "Tester",
        Status = status,
        ReferenceNumber = Guid.NewGuid().ToString("N")[..12],
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove",
    };

    private async Task SeedWindowsAndRequestsAsync(IEnumerable<CheckingWindow> windows, IEnumerable<ChangeRequest> requests)
    {
        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.AddRange(windows);
        ctx.ChangeRequests.AddRange(requests);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ExcludesWhenPupilHasMatchingChangeRequest()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var upn = "A100000000001";

        await SeedWindowsAndRequestsAsync(
            [NewWindow(windowId)],
            [NewChangeRequest(windowId, TestUrnLong, upn)]);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [NewPupil(windowId, "Smith", upn)]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);

        Assert.Empty(results);
    }

    [Fact]
    public async Task IncludesWhenChangeRequestIsForDifferentWindow()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var otherWindowId = Guid.NewGuid();
        var upn = "A100000000002";

        await SeedWindowsAndRequestsAsync(
            [NewWindow(windowId), NewWindow(otherWindowId)],
            [NewChangeRequest(otherWindowId, TestUrnLong, upn)]);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [NewPupil(windowId, "Jones", upn)]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Jones", PupilFilter.Included);

        Assert.Single(results);
    }

    [Fact]
    public async Task IncludesWhenChangeRequestIsForDifferentOrg()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var otherUrnLong = TestUrnLong + 1;
        var upn = "A100000000003";

        await SeedWindowsAndRequestsAsync(
            [NewWindow(windowId)],
            [NewChangeRequest(windowId, otherUrnLong, upn)]);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [NewPupil(windowId, "Taylor", upn)]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Taylor", PupilFilter.Included);

        Assert.Single(results);
    }

    [Fact]
    public async Task ExcludesWhenChangeRequestIsInProgress()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var upn = "A100000000004";

        await SeedWindowsAndRequestsAsync(
            [NewWindow(windowId)],
            [NewChangeRequest(windowId, TestUrnLong, upn, status: RequestStatus.InProgress)]);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [NewPupil(windowId, "Brown", upn)]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Brown", PupilFilter.Included);

        Assert.Empty(results);
    }

    [Fact]
    public async Task FilterAll_ReturnsBothIncludedAndNonIncludedPupils()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await SeedWindowsAndRequestsAsync([NewWindow(windowId)], []);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab,
        [
            NewPupil(windowId, "Wilson", "A100000000005", pincl: 401),  // included
            NewPupil(windowId, "Wilson", "A100000000006", pincl: 999),  // non-included
        ]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Wilson", PupilFilter.All);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task FilterIncluded_ReturnsOnlyIncludedPupils()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await SeedWindowsAndRequestsAsync([NewWindow(windowId)], []);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab,
        [
            NewPupil(windowId, "Wilson", "A100000000007", pincl: 401),  // included
            NewPupil(windowId, "Wilson", "A100000000008", pincl: 999),  // non-included
        ]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Wilson", PupilFilter.Included);

        Assert.Single(results);
    }

    [Fact]
    public async Task FilterNonIncluded_ReturnsOnlyNonIncludedPupils()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await SeedWindowsAndRequestsAsync([NewWindow(windowId)], []);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab,
        [
            NewPupil(windowId, "Wilson", "A100000000011", pincl: 401),  // included
            NewPupil(windowId, "Wilson", "A100000000012", pincl: 999),  // non-included
        ]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Wilson", PupilFilter.NonIncluded);

        Assert.Single(results);
    }

    [Fact]
    public async Task ExcludeId_ExcludesSpecificPupilFromResults()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var pupilToExclude = NewPupil(windowId, "Adams", "A100000000009");
        var otherPupil = NewPupil(windowId, "Adams", "A100000000010");

        await SeedWindowsAndRequestsAsync([NewWindow(windowId)], []);

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [pupilToExclude, otherPupil]);

        var repo = CreateRepo(blobClient);
        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Adams", PupilFilter.All, excludeId: pupilToExclude.Id);

        Assert.Single(results);
        Assert.Equal(otherPupil.Id, results[0].Id);
    }
}
