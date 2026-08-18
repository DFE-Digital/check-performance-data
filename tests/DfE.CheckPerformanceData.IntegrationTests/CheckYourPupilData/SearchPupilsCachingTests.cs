using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// AB#296648 PR review: SearchPupilsAsync used to resolve the window on every call, so every
// autocomplete keystroke hit CheckingWindows even though the pupil list itself is cached. The
// fix caches the window type alongside the pupils so SearchPupilsAsync never queries the window
// once the pupil cache is warm.
[Collection(nameof(PostgresCollection))]
public sealed class SearchPupilsCachingTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";

    private CheckYourPupilDataRepository CreateRepo(IPupilDataBlobClient blobClient, IMemoryCache cache)
        => new(fixture.CreateContext(), blobClient, cache);

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

    private static DateTime Unspecified(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    private static CheckingWindow NewWindow(Guid id, CheckingWindowType type) => new()
    {
        Id = id,
        Title = "Test Window",
        StartDate = Unspecified(DateTime.UtcNow.AddDays(-7)),
        EndDate = Unspecified(DateTime.UtcNow.AddDays(7)),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = type,
    };

    private static PupilRecord NewPupil(Guid windowId, string surname, string upn) => new()
    {
        Id = Guid.NewGuid(),
        CheckingWindowId = windowId,
        Urn = long.Parse(TestUrn),
        Laestab = TestLaestab,
        Surname = surname,
        Firstname = "Test",
        Sex = "M",
        DateOfBirth = "01/01/2000",
        Age = 16,
        FirstLanguage = "English",
        Pincl = 401,
        NewMobile = false,
        ActualYearGroup = "11",
        Ethnicity = "A1",
        SenF = "N",
        EntryDate = "01/09/2021",
        Cypmd_Id = Guid.NewGuid().ToString(),
        MatchRef = 1,
        Upn = upn,
    };

    private async Task<Guid> SeedWindowAsync(CheckingWindowType type)
    {
        var windowId = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId, type));
        await ctx.SaveChangesAsync();
        return windowId;
    }

    private async Task DeleteWindowAsync(Guid windowId)
    {
        await using var ctx = fixture.CreateContext();
        var window = new CheckingWindow { Id = windowId };
        ctx.CheckingWindows.Attach(window);
        ctx.CheckingWindows.Remove(window);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task SecondSearch_ForSameWindowAndSchool_DoesNotReQueryTheWindow()
    {
        await ResetAsync();
        var windowId = await SeedWindowAsync(CheckingWindowType.KS4June);
        var pupil = NewPupil(windowId, "Smith", "A100000000001");

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [pupil]);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = CreateRepo(blobClient, cache);

        // Warms the pupil (and window-type) cache.
        var first = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);
        Assert.Single(first);

        // The window row is gone, so if SearchPupilsAsync still queried CheckingWindows here,
        // GetCheckingWindowAsync's SingleAsync would throw. A cached window type means this
        // still succeeds.
        await DeleteWindowAsync(windowId);

        var second = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);

        Assert.Single(second);
        Assert.Equal(pupil.Id, second[0].Id);
    }

    [Fact]
    public async Task SecondSearch_ForSameWindowAndSchool_StillUsesCorrectWindowTypeLabel()
    {
        await ResetAsync();
        var windowId = await SeedWindowAsync(CheckingWindowType.KS4June);
        var pupil = NewPupil(windowId, "Smith", "A100000000001");

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [pupil]);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var repo = CreateRepo(blobClient, cache);

        await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);
        await DeleteWindowAsync(windowId);

        var second = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);

        // KS4June's label format (surname, forename, DOB) — proves the cached window type,
        // not a default/blank one, drove the second call's formatting.
        Assert.Equal("Smith, Test, 01/01/2000", second[0].Label);
    }
}
