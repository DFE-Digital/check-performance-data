using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

[Collection(nameof(PostgresCollection))]
public sealed class CheckingDataPocTests(PostgresFixture postgres, AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task Full_lifecycle_preserves_both_imports_lineage_and_fallback_without_reingress()
    {
        await using var db = postgres.CreateContext();
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        var processor = new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient> { ["app"] = blobs });
        var ingress = new CheckingExerciseIngress(new CheckingExerciseDefinitionRepository(db, new WindowRepository(db)), processor, new Clock());
        var catalogue = new CheckingDataCatalogue(db, new Clock());
        var reader = new CheckingDataReader(blobs);
        var resolver = new CheckingExerciseStorageResolver(new WindowRepository(db), new Clock());
        var legacyReader = new CheckYourPupilDataRepository(db, new PupilDataBlobClient(blobs, resolver), new MemoryCache(new MemoryCacheOptions()));
        var resultsReader = new StudentResultsBlobClient(blobs, new MemoryCache(new MemoryCacheOptions()), resolver);
        await Seed(CheckingExercisePocState.ProvisionalOpen);
        var provisional = await Visible();
        Assert.Equal(new[] { "Students", "Results" }, provisional.Select(e => e.TabName));
        Assert.All(provisional, e => Assert.True(e.CanAct(Now)));
        Assert.Equal(4, (await legacyReader.GetAllPupilsForSchoolAsync(CheckingExercisePocSeed.WindowId, CheckingExercisePocSeed.Laestab)).Count);
        Assert.Equal(4, (await resultsReader.GetStudentIdsWithResultsAsync(CheckingExercisePocSeed.WindowId, CheckingExercisePocSeed.Laestab)).Count);
        var students = provisional.Single(e => e.DataType == CheckingDataType.Pupil);
        var original = await reader.ReadAsync(students, CheckingExercisePocSeed.Laestab, default);
        var results = provisional.Single(e => e.DataType == CheckingDataType.Results);
        using (var resultJson = JsonDocument.Parse((await reader.ReadAsync(results, CheckingExercisePocSeed.Laestab, default))!))
        {
            var result = resultJson.RootElement[0];
            Assert.Equal("B", result.GetProperty("GRADE").GetString());
            Assert.False(result.TryGetProperty("INCLUDED", out _));
        }
        Assert.Equal(new[] { "A", "B", "C", "D" }, Names(original!));

        await Seed(CheckingExercisePocState.ProvisionalClosed);
        Assert.All(await Visible(), e => Assert.False(e.CanAct(Now)));
        Assert.Equal(original, await reader.ReadAsync(students, CheckingExercisePocSeed.Laestab, default));

        await Seed(CheckingExercisePocState.RevisedClosed);
        var revised = await Visible();
        Assert.Equal(2, revised.Count);
        Assert.All(revised, e => { Assert.Equal("Revised", e.Stage); Assert.False(e.CanAct(Now)); });
        Assert.Equal(3, (await legacyReader.GetAllPupilsForSchoolAsync(CheckingExercisePocSeed.WindowId, CheckingExercisePocSeed.Laestab)).Count);
        Assert.Equal(3, (await resultsReader.GetStudentIdsWithResultsAsync(CheckingExercisePocSeed.WindowId, CheckingExercisePocSeed.Laestab)).Count);
        var revisedStudents = revised.Single(e => e.DataType == CheckingDataType.Pupil);
        Assert.Equal(students.Id, revisedStudents.ReplacesCheckingExerciseId);
        Assert.Equal(new[] { "A", "B", "C" }, Names((await reader.ReadAsync(revisedStudents, CheckingExercisePocSeed.Laestab, default))!));
        Assert.Equal(original, await reader.ReadAsync(students, CheckingExercisePocSeed.Laestab, default));

        // Re-ingress with the destructive clear option must still be confined to Revised.
        var revisedEntity = await db.Set<DfE.CheckPerformanceData.Persistence.Entities.CheckingExercise>()
            .Include(e => e.Datasets).SingleAsync(e => e.Id == revisedStudents.Id);
        Assert.Equal(2, revisedEntity.Datasets.Count);
        ValidationProgress? last = null;
        await foreach (var progress in ingress.ProcessAsync(revisedEntity.Id, clearExistingFiles: true)) last = progress;
        Assert.True(last is { IsComplete: true, IsError: false });
        Assert.Equal(original, await reader.ReadAsync(students, CheckingExercisePocSeed.Laestab, default));

        await Seed(CheckingExercisePocState.RevisedOpen);
        Assert.All(await Visible(), e => Assert.True(e.CanAct(Now)));
        await Seed(CheckingExercisePocState.Fallback);
        var fallback = await Visible();
        Assert.Equal(4, (await legacyReader.GetAllPupilsForSchoolAsync(CheckingExercisePocSeed.WindowId, CheckingExercisePocSeed.Laestab)).Count);
        Assert.Equal(4, (await resultsReader.GetStudentIdsWithResultsAsync(CheckingExercisePocSeed.WindowId, CheckingExercisePocSeed.Laestab)).Count);
        Assert.Equal(students.Id, new CheckingExerciseService(new Clock()).IdFor((await legacyReader.GetCheckingWindowAsync(CheckingExercisePocSeed.WindowId)).Exercises, CheckingExerciseType.PupilData));
        Assert.Equal(provisional.Select(e => e.Id), fallback.Select(e => e.Id));
        Assert.Equal(original, await reader.ReadAsync(students, CheckingExercisePocSeed.Laestab, default));
        Assert.Equal(4, await db.CheckingWindows.Where(w => w.Id == CheckingExercisePocSeed.ProvisionalWindowId
            || w.Id == CheckingExercisePocSeed.RevisedWindowId).SelectMany(w => w.CheckingExercises).CountAsync());
        Assert.Equal(students.Id, (await db.Set<DfE.CheckPerformanceData.Persistence.Entities.CheckingExercise>()
            .SingleAsync(e => e.Id == revisedStudents.Id)).ReplacesCheckingExerciseId);

        // Query translation, ordering and date boundaries are exercised against PostgreSQL.
        var pupilEntity = await db.Set<DfE.CheckPerformanceData.Persistence.Entities.CheckingExercise>().SingleAsync(e => e.Id == students.Id);
        pupilEntity.TabOrder = 10;
        pupilEntity.VisibleFrom = Now;
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "Results", "Students" }, (await Visible()).Select(e => e.TabName));
        pupilEntity.VisibleUntil = Now;
        await db.SaveChangesAsync();
        Assert.Equal("Results", Assert.Single(await Visible()).TabName);
        pupilEntity.VisibleUntil = null;
        pupilEntity.VisibleFrom = Now.AddTicks(10);
        await db.SaveChangesAsync();
        Assert.Equal("Results", Assert.Single(await Visible()).TabName);

        // Removing a configured exercise from the legacy wizard hides it, preserving lineage/data.
        var windows = new WindowRepository(db);
        var windowDto = (await windows.GetByIdAsync(students.WindowId, default))!;
        windowDto.Exercises.RemoveAll(e => e.Id == students.Id);
        await windows.UpdateAsync(windowDto, default);
        Assert.False(pupilEntity.IsEnabled);
        Assert.True(await db.Set<DfE.CheckPerformanceData.Persistence.Entities.CheckingExercise>().AnyAsync(e => e.Id == students.Id));
        Assert.Equal(original, await reader.ReadAsync(students, CheckingExercisePocSeed.Laestab, default));

        async Task<IReadOnlyList<CheckingDataExercise>> Visible() =>
            (await catalogue.GetVisibleAsync(default)).Where(e => e.WindowId == CheckingExercisePocSeed.WindowId).ToList();

        Task Seed(CheckingExercisePocState state) => CheckingExercisePocSeed.ApplyAsync(db, blobs, ingress, state, Now);
    }

    private static string?[] Names(byte[] data)
    {
        using var json = JsonDocument.Parse(data);
        return json.RootElement.EnumerateArray().Select(r => r.GetProperty("SURNAME").GetString()).OrderBy(n => n).ToArray();
    }
}
