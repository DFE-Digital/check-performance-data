using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// ChangeRequests gains a CheckingExerciseId so a row can say which checking exercise it belongs to,
// not merely which window. Rows written before the column existed still have to answer that
// question, so the migration backfills them through the same mapping the write path uses
// (WhatToChangeCheckingExerciseMap, restated as SQL because a migration cannot call Application).
//
// Each test gets its own container because they apply the chain only part-way.
public sealed class ChangeRequestCheckingExerciseBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // The migration immediately before the column is introduced.
    private const string BeforeColumn = "20260902081430_AddChangeRequestDecisionTrace";
    private const string ColumnMigration = "20260902124522_AddChangeRequestCheckingExerciseId";

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

    private async Task<Guid?> ReadExerciseIdAsync(string referenceNumber)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """SELECT "CheckingExerciseId" FROM "ChangeRequests" WHERE "ReferenceNumber" = @r;""";
        cmd.Parameters.AddWithValue("r", referenceNumber);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull or null ? null : (Guid)value;
    }

    // A 16-19 shaped window: two exercises, on ranges that do not coincide. This is the only shape
    // where WindowId alone cannot answer which exercise a request belongs to.
    private async Task<(Guid WindowId, Guid PupilDataId, Guid ResultsEnquiryId)> InsertWindowAsync()
    {
        var windowId = Guid.NewGuid();
        var pupilDataId = Guid.NewGuid();
        var enquiryId = Guid.NewGuid();

        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "StartDate", "EndDate", "KeyStage", "CheckingWindowType", "Title",
                 "Published", "IngressFile", "SchemaFile", "IngressFileChecksum", "SchemaFileChecksum")
            VALUES ('{windowId}', '2026-10-01 00:00:00', '2027-03-31 17:00:00', 'Post16', 'Post16',
                    '16 to 19 2026', false, '', '', '', '');

            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            VALUES ('{pupilDataId}', '{windowId}', 'PupilData',
                    '2026-10-01 00:00:00', '2026-11-30 17:00:00', 0),
                   ('{enquiryId}', '{windowId}', 'ResultsEnquiry',
                    '2027-01-01 00:00:00', '2027-03-31 17:00:00', 1);
            """);

        return (windowId, pupilDataId, enquiryId);
    }

    private async Task InsertRequestAsync(
        Guid windowId, string referenceNumber, string requestType, string? amendmentType)
    {
        var amendment = amendmentType is null ? "NULL" : $"'{amendmentType}'";
        await ExecuteAsync($"""
            INSERT INTO "ChangeRequests"
                ("Id", "WindowId", "OrganisationUrn", "Submitted", "SubmittedById", "SubmittedByName",
                 "Status", "ReferenceNumber", "RequestType", "RequestTypeDescription", "AmendmentType")
            VALUES (gen_random_uuid(), '{windowId}', 142313, '2026-10-15 09:00:00',
                    '11111111-1111-1111-1111-111111111111', 'Ada Editor', 'SubmittedUnCommitted',
                    '{referenceNumber}', '{requestType}', 'seeded', {amendment});
            """);
    }

    [Fact]
    public async Task An_amendment_is_backfilled_onto_the_pupil_data_exercise()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeColumn);
        var (windowId, pupilDataId, _) = await InsertWindowAsync();
        await InsertRequestAsync(windowId, "REF-REMOVE", "Amendment", "Remove");

        await migrator.MigrateAsync(ColumnMigration);

        Assert.Equal(pupilDataId, await ReadExerciseIdAsync("REF-REMOVE"));
    }

    [Theory]
    [InlineData("IncorrectGrade")]
    [InlineData("MissingQualification")]
    public async Task A_results_enquiry_is_backfilled_onto_the_results_enquiry_exercise(
        string amendmentType)
    {
        // These are the two WhatToChange members WhatToChangeCheckingExerciseMap sends to the
        // enquiry; the CASE in the migration must name exactly the same pair.
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeColumn);
        var (windowId, _, enquiryId) = await InsertWindowAsync();
        await InsertRequestAsync(windowId, "REF-ENQUIRY", "ResultsEnquiry", amendmentType);

        await migrator.MigrateAsync(ColumnMigration);

        Assert.Equal(enquiryId, await ReadExerciseIdAsync("REF-ENQUIRY"));
    }

    [Fact]
    public async Task A_confirm_correct_declaration_is_backfilled_onto_the_pupil_data_exercise()
    {
        // It carries no AmendmentType at all, and confirming the data is correct is a pupil-data
        // action by definition — so the CASE's ELSE has to catch it rather than leaving it null.
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeColumn);
        var (windowId, pupilDataId, _) = await InsertWindowAsync();
        await InsertRequestAsync(windowId, "REF-CONFIRM", "ConfirmCorrect", amendmentType: null);

        await migrator.MigrateAsync(ColumnMigration);

        Assert.Equal(pupilDataId, await ReadExerciseIdAsync("REF-CONFIRM"));
    }

    [Fact]
    public async Task A_row_whose_window_lacks_the_mapped_exercise_stays_null()
    {
        // Fails closed, the same answer ICheckingExerciseService gives: a KS2 window has no results
        // enquiry, so an enquiry row against one is left alone rather than pointed at its neighbour.
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeColumn);
        var (windowId, _, enquiryId) = await InsertWindowAsync();
        await ExecuteAsync($"""DELETE FROM "CheckingExercises" WHERE "Id" = '{enquiryId}';""");
        await InsertRequestAsync(windowId, "REF-ORPHAN", "ResultsEnquiry", "IncorrectGrade");

        await migrator.MigrateAsync(ColumnMigration);

        Assert.Null(await ReadExerciseIdAsync("REF-ORPHAN"));
    }

    [Fact]
    public async Task Deleting_an_exercise_nulls_the_stamp_rather_than_blocking_the_delete()
    {
        // The admin wizard deletes the CheckingExercises rows an admin unticks, so the FK is
        // ON DELETE SET NULL. Restrict would surface as an unhandled DbUpdateException mid-wizard.
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        var (windowId, pupilDataId, _) = await InsertWindowAsync();
        await InsertRequestAsync(windowId, "REF-ORPHANED-LATER", "Amendment", "Remove");
        await ExecuteAsync($"""
            UPDATE "ChangeRequests" SET "CheckingExerciseId" = '{pupilDataId}'
            WHERE "ReferenceNumber" = 'REF-ORPHANED-LATER';
            """);

        await ExecuteAsync($"""DELETE FROM "CheckingExercises" WHERE "Id" = '{pupilDataId}';""");

        Assert.Null(await ReadExerciseIdAsync("REF-ORPHANED-LATER"));
    }
}
