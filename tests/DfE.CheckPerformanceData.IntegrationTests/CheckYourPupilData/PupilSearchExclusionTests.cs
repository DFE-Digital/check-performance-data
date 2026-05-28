using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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

    private static Pupil NewPupil(Guid windowId, string urn, string surname, string upn) => new()
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
        Pincl = IncludedPincl,
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
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Smith", included: true);

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
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Jones", included: true);

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
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Taylor", included: true);

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
        var results = await repo.SearchPupilsAsync(windowId, TestUrn, "Brown", included: true);

        Assert.Empty(results);
    }
}
