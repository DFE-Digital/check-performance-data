using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

[Collection(nameof(PostgresCollection))]
public sealed class PupilSearchExclusionTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private static readonly long TestUrnLong = long.Parse(TestUrn);
    private const int IncludedPincl = 401;

    private CheckYourPupilDataRepository CreateRepo()
    {
        var ctx = fixture.CreateContext();
        return new CheckYourPupilDataRepository(ctx);
    }

    private async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // ChangeRequests has FK to CheckingWindows; Pupils also reference CheckingWindows.
        // Truncate in dependency order.
        cmd.CommandText = @"
            TRUNCATE ""ChangeRequests"" CASCADE;
            TRUNCATE ""Pupils"" CASCADE;
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

    private static Pupil NewPupil(Guid windowId, string urn, string surname, string upn, int pincl = IncludedPincl) => new()
    {
        Id = Guid.NewGuid(),
        CheckingWindowId = windowId,
        Urn = urn,
        Laestab = "1234567",
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

    private static ChangeRequest NewChangeRequest(Guid windowId, long orgUrn, string upn, RequestStatus status = RequestStatus.Submitted) => new()
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
        RequestType = "Remove",
    };

    [Fact]
    public async Task ExcludesWhenPupilHasMatchingChangeRequest()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var upn = "A100000000001";

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Smith", upn));
        ctx.ChangeRequests.Add(NewChangeRequest(windowId, TestUrnLong, upn));
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Smith", PupilFilter.Included);

        Assert.Empty(results);
    }

    [Fact]
    public async Task IncludesWhenChangeRequestIsForDifferentWindow()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var otherWindowId = Guid.NewGuid();
        var upn = "A100000000002";

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.CheckingWindows.Add(NewWindow(otherWindowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Jones", upn));
        ctx.ChangeRequests.Add(NewChangeRequest(otherWindowId, TestUrnLong, upn));
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Jones", PupilFilter.Included);

        Assert.Single(results);
    }

    [Fact]
    public async Task IncludesWhenChangeRequestIsForDifferentOrg()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var otherUrnLong = TestUrnLong + 1;
        var upn = "A100000000003";

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Taylor", upn));
        ctx.ChangeRequests.Add(NewChangeRequest(windowId, otherUrnLong, upn));
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Taylor", PupilFilter.Included);

        Assert.Single(results);
    }

    [Fact]
    public async Task ExcludesWhenChangeRequestIsDraft()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var upn = "A100000000004";

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Brown", upn));
        ctx.ChangeRequests.Add(NewChangeRequest(windowId, TestUrnLong, upn, status: RequestStatus.Draft));
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Brown", PupilFilter.Included);

        Assert.Empty(results);
    }

    [Fact]
    public async Task FilterAll_ReturnsBothIncludedAndNonIncludedPupils()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Wilson", "A100000000005", pincl: 401));  // included
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Wilson", "A100000000006", pincl: 999));  // non-included
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Wilson", PupilFilter.All);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task FilterIncluded_ReturnsOnlyIncludedPupils()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Wilson", "A100000000007", pincl: 401));  // included
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, "Wilson", "A100000000008", pincl: 999));  // non-included
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Wilson", PupilFilter.Included);

        Assert.Single(results);
    }

    [Fact]
    public async Task ExcludeId_ExcludesSpecificPupilFromResults()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var pupilToExclude = NewPupil(windowId, TestUrn, "Adams", "A100000000009");
        var otherPupil = NewPupil(windowId, TestUrn, "Adams", "A100000000010");

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(pupilToExclude);
        ctx.Pupils.Add(otherPupil);
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Adams", PupilFilter.All, excludeId: pupilToExclude.Id);

        Assert.Single(results);
        Assert.Equal(otherPupil.Id, results[0].Id);
    }
}
