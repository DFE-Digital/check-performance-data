using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #317 makes the check-your-pupil-data page offer "Report an issue with an exam result" only while a
// ResultsEnquiry exercise is open. #313's backfill gave every existing window a PupilData row and
// nothing else, so without this second backfill the option would vanish from every deployed 16-19
// window the moment #317 ships — a shipped feature silently withdrawn. This gives Post16 windows the
// exercise they are already behaving as if they had, on the window's own dates. #319's admin sets
// the real dates; until then the option appears for exactly the range it appears for today.
public sealed class ResultsEnquiryExerciseBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // The window has to exist before #313 runs, so that its own PupilData backfill gives the window
    // the row a deployed database would already have. Inserting after #313 would leave the window
    // with no exercises at all, which is not the state this migration has to cope with.
    private const string BeforeExercises = "20260804204028_AddChangeRequestAmendmentType";
    private const string BeforeBackfill = "20260819164322_ReparentDatasetsOntoCheckingExercise";
    private const string BackfillMigration = "20260820081648_BackfillResultsEnquiryExercise";

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<(string ExerciseType, DateTime StartDate, DateTime EndDate, int SortOrder)>>
        ReadExercisesAsync(Guid windowId)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "ExerciseType", "StartDate", "EndDate", "SortOrder"
            FROM "CheckingExercises" WHERE "CheckingWindowId" = @id ORDER BY "SortOrder";
            """;
        cmd.Parameters.AddWithValue("id", windowId);

        List<(string, DateTime, DateTime, int)> rows = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetDateTime(1), reader.GetDateTime(2), reader.GetInt32(3)));
        }
        return rows;
    }

    private async Task<Guid> InsertWindowAsync(string windowType, string keyStage)
    {
        var windowId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "StartDate", "EndDate", "KeyStage", "CheckingWindowType", "Title",
                 "Published", "IngressFile", "SchemaFile", "IngressFileChecksum", "SchemaFileChecksum")
            VALUES ('{windowId}', '2026-10-07 00:00:00', '2027-03-31 17:00:00', '{keyStage}', '{windowType}',
                    'Existing window', false, 'pupils.csv', 'pupils-schema.json', 'CSVSUM', 'SCHEMASUM');
            """);
        return windowId;
    }

    [Fact]
    public async Task A_16_to_19_window_gains_a_results_enquiry_exercise_on_the_windows_own_dates()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertWindowAsync("Post16", "Post16");
        await migrator.MigrateAsync(BeforeBackfill);

        await migrator.MigrateAsync(BackfillMigration);

        var exercises = await ReadExercisesAsync(windowId);
        var enquiry = Assert.Single(exercises, e => e.ExerciseType == "ResultsEnquiry");
        Assert.Equal(new DateTime(2026, 10, 7, 0, 0, 0), enquiry.StartDate);
        Assert.Equal(new DateTime(2027, 3, 31, 17, 0, 0), enquiry.EndDate);
        // After the pupil-data row, which #313 backfilled at SortOrder 0.
        Assert.Equal(1, enquiry.SortOrder);
    }

    [Fact]
    public async Task The_pupil_data_exercise_is_left_exactly_as_it_was()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertWindowAsync("Post16", "Post16");
        await migrator.MigrateAsync(BeforeBackfill);

        await migrator.MigrateAsync(BackfillMigration);

        var pupilData = Assert.Single(await ReadExercisesAsync(windowId), e => e.ExerciseType == "PupilData");
        Assert.Equal(new DateTime(2026, 10, 7, 0, 0, 0), pupilData.StartDate);
        Assert.Equal(new DateTime(2027, 3, 31, 17, 0, 0), pupilData.EndDate);
        Assert.Equal(0, pupilData.SortOrder);
    }

    [Theory]
    [InlineData("KS4June", "KS4")]
    [InlineData("KS4Autumn", "KS4")]
    [InlineData("KS2", "KS2")]
    public async Task Other_window_types_gain_nothing(string windowType, string keyStage)
    {
        // They do not offer an enquiry today, and #317 must not start offering one for them by
        // accident — that is a product decision for #319's admin, not for a migration.
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertWindowAsync(windowType, keyStage);
        await migrator.MigrateAsync(BeforeBackfill);

        await migrator.MigrateAsync(BackfillMigration);

        var exercise = Assert.Single(await ReadExercisesAsync(windowId));
        Assert.Equal("PupilData", exercise.ExerciseType);
    }

    [Fact]
    public async Task A_window_that_already_has_the_exercise_is_untouched()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertWindowAsync("Post16", "Post16");
        await migrator.MigrateAsync(BeforeBackfill);

        // A hand-configured window with real enquiry dates must keep them: the backfill exists to
        // stop a regression, not to overwrite a decision someone has already made.
        await ExecuteAsync($"""
            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            VALUES (gen_random_uuid(), '{windowId}', 'ResultsEnquiry',
                    '2026-11-01 00:00:00', '2026-11-30 17:00:00', 5);
            """);

        await migrator.MigrateAsync(BackfillMigration);

        var enquiry = Assert.Single(await ReadExercisesAsync(windowId), e => e.ExerciseType == "ResultsEnquiry");
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0), enquiry.StartDate);
        Assert.Equal(5, enquiry.SortOrder);
    }
}
