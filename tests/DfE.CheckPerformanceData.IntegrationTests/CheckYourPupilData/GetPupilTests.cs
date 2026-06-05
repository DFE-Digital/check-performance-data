using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

[Collection(nameof(PostgresCollection))]
public sealed class GetPupilTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string OtherUrn = "999999";
    private const int IncludedPincl = 401;

    private CheckYourPupilDataRepository CreateRepo()
    {
        var ctx = fixture.CreateContext();
        return new CheckYourPupilDataRepository(ctx);
    }

    private async Task ResetAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE ""ChangeRequests"" CASCADE;
            TRUNCATE ""Pupils"" CASCADE;
            TRUNCATE ""CheckingWindows"" RESTART IDENTITY CASCADE;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

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

    private static Pupil NewPupil(Guid windowId, string urn, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CheckingWindowId = windowId,
        Urn = urn,
        Laestab = "1234567",
        Surname = "Smith",
        Firstname = "Jane",
        Sex = "F",
        DateOfBirth = "01/01/2010",
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
        Upn = $"U{Guid.NewGuid():N}"[..13],
    };

    [Fact]
    public async Task GetPupilAsync_WhenUrnMatches_ReturnsPupil()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var pupilId = Guid.NewGuid();

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, pupilId));
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        var result = await repo.GetPupilAsync(windowId, TestUrn, pupilId);

        Assert.Equal(pupilId, result.Id);
    }

    [Fact]
    public async Task GetPupilAsync_WhenUrnDoesNotMatch_Throws()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();
        var pupilId = Guid.NewGuid();

        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(NewWindow(windowId));
        ctx.Pupils.Add(NewPupil(windowId, TestUrn, pupilId));
        await ctx.SaveChangesAsync();

        var repo = CreateRepo();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.GetPupilAsync(windowId, OtherUrn, pupilId));
    }
}
