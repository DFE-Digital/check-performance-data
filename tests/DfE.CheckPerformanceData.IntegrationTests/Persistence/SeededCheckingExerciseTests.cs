using System.Security.Cryptography;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.Extensions.Logging.Abstractions;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// The dev seed has to produce a window that runs two activities on two different date ranges, or
// none of #315-#320 can be developed or demoed locally. It also has to obey the rule that holds the
// model together: a window's outer dates are the union of its exercises' dates. The one 16-19
// window, "16 to 19 Oct", is set up for the start of the results enquiry and left for an admin to
// import; the KS4 windows are fixtures, ingested by the seed.
[Collection(nameof(AzuriteCollection))]
public sealed class SeededCheckingExerciseTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly Guid _openKs4 = Guid.NewGuid();
    private readonly Guid _closedKs4 = Guid.NewGuid();
    private readonly Guid _october = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _october);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task<CheckingWindow> LoadAsync(Guid windowId)
    {
        await using var ctx = CreateContext();
        return await ctx.CheckingWindows
            .Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .AsNoTracking()
            .SingleAsync(w => w.Id == windowId);
    }

    [Fact]
    public async Task The_October_window_runs_pupil_data_and_results_enquiry_on_different_ranges()
    {
        var window = await LoadAsync(_october);
        var now = DateTime.Now;

        Assert.Equal("16 to 19 Oct", window.Title);
        Assert.Equal(CheckingWindowType.Post16, window.CheckingWindowType);
        var exercises = window.CheckingExercises.OrderBy(e => e.SortOrder).ToList();
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            exercises.Select(e => e.ExerciseType));
        Assert.All(exercises, e =>
        {
            Assert.True(e.IsEnabled, $"{e.Name} is not enabled");
            Assert.True(e.StartDate <= now && e.EndDate > now, $"{e.Name} is not open");
        });
        // The results enquiry runs to the end of March, long after pupil data checking shuts.
        Assert.Equal((3, 31), (exercises[1].EndDate.Month, exercises[1].EndDate.Day));
        Assert.True(exercises[1].EndDate > exercises[0].EndDate.AddMonths(3));
        Assert.Equal(new DateTime(now.Year + 1, 10, 1), window.NextOpportunity);
    }

    [Fact]
    public async Task The_October_window_has_every_slot_but_no_data()
    {
        // Left for an admin to import and validate: nothing is linked, nothing has run.
        var window = await LoadAsync(_october);

        var students = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
        Assert.Equal(["included", "nonincluded"], students.Datasets.OrderBy(d => d.SortOrder).Select(d => d.Name));

        var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        Assert.Equal(
            DfE.CheckPerformanceData.Application.ResultsEnquiry.ResultsSources.For(CheckingWindowType.Post16).Select(s => s.Tag),
            results.Datasets.OrderBy(d => d.SortOrder).Select(d => d.Name));

        Assert.All(window.CheckingExercises, e =>
        {
            Assert.Null(e.CurrentReleaseId);
            Assert.Null(e.Validated);
            Assert.All(e.Datasets, d => Assert.True(d.IngressFile == string.Empty && d.SchemaFile == string.Empty, d.Name));
        });
    }

    [Theory]
    [InlineData(0)] // open KS4 June
    [InlineData(1)] // closed KS4 June
    public async Task A_single_activity_window_gets_one_pupil_data_exercise_on_its_own_dates(int which)
    {
        var window = await LoadAsync(which == 0 ? _openKs4 : _closedKs4);

        var exercise = Assert.Single(window.CheckingExercises);
        Assert.Equal(CheckingExerciseType.PupilData, exercise.ExerciseType);
        Assert.Equal(window.StartDate, exercise.StartDate);
        Assert.Equal(window.EndDate, exercise.EndDate);
    }

    [Fact]
    public async Task Every_seeded_windows_outer_dates_equal_the_union_of_its_exercises()
    {
        foreach (var windowId in new[] { _openKs4, _closedKs4, _october })
        {
            var window = await LoadAsync(windowId);

            Assert.NotEmpty(window.CheckingExercises);
            Assert.Equal(window.StartDate, window.CheckingExercises.Min(e => e.StartDate));
            Assert.Equal(window.EndDate, window.CheckingExercises.Max(e => e.EndDate));
        }
    }

    [Fact]
    public async Task Window_seed_can_be_repeated()
    {
        // The dev seed deletes every window on each start.
        for (var start = 0; start < 2; start++)
        {
            await using var ctx = CreateContext();
            await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _october);
        }

        Assert.Equal(2, (await LoadAsync(_october)).CheckingExercises.Count);
    }

    [Fact]
    public async Task The_October_sample_files_import_and_validate_into_the_October_window()
    {
        // What the admin does by hand: choose each sample CSV and schema for its slot, then
        // validate. The journey then finds the students and every October result, each with the
        // tag of the file it came in, and the second late results file is still awaited.
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        var schemas = Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "post16");
        var samples = SeedPost16OctoberSamples.Files();
        try
        {
            await using (var ctx = CreateContext())
            {
                var window = await ctx.CheckingWindows
                    .Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
                    .SingleAsync(w => w.Id == _october);
                var container = blobs.GetBlobContainerClient(_october.ToString());
                await container.CreateIfNotExistsAsync();

                var students = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
                await LinkAsync(students, "included", "students/included.csv", "students-included_schema.json");
                await LinkAsync(students, "nonincluded", "students/nonincluded.csv", "students-non-included_schema.json");
                var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
                await LinkAsync(results, "16to19_INC", "results/16to19_INC.csv", "results-included_schema.json");
                await LinkAsync(results, "16to19_NONINC", "results/16to19_NONINC.csv", "results-non-included_schema.json");
                await LinkAsync(results, "16to19_LR1", "results/16to19_LR1.csv", "results-late_schema.json");
                await ctx.SaveChangesAsync();

                async Task LinkAsync(CheckingExercise exercise, string slot, string csv, string schema)
                {
                    var dataset = exercise.Datasets.Single(d => d.Name == slot);
                    (dataset.IngressFile, dataset.IngressFileChecksum) = await UploadAsync(exercise, dataset, csv, samples[csv]);
                    (dataset.SchemaFile, dataset.SchemaFileChecksum) = await UploadAsync(exercise, dataset, schema,
                        await File.ReadAllBytesAsync(Path.Combine(schemas, schema)));
                }

                async Task<(string, string)> UploadAsync(CheckingExercise exercise, CheckingWindowDataset dataset, string name, byte[] content)
                {
                    var path = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, name);
                    await container.GetBlobClient(path).UploadAsync(new BinaryData(content), overwrite: true);
                    return (path, Convert.ToHexString(SHA256.HashData(content)));
                }
            }

            await using (var ctx = CreateContext())
            {
                var ingress = new CheckingExerciseIngress(
                    new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
                    new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                        new Dictionary<string, BlobServiceClient> { ["app"] = blobs }), TimeProvider.System);
                foreach (var exercise in (await LoadAsync(_october)).CheckingExercises)
                {
                    ValidationProgress? last = null;
                    await foreach (var progress in ingress.ProcessAsync(exercise.Id)) last = progress;
                    Assert.True(last is { IsComplete: true, IsError: false }, $"{exercise.Name}: {last?.Message}");
                }
            }

            var october = await LoadAsync(_october);
            var studentsExercise = october.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            var pupils = DfE.CheckPerformanceData.Infrastructure.BlobStorage.PupilDataBlobClient.Deserialize(
                await ReadAsync(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(studentsExercise.Id, CheckingDataType.Pupil, "860/4070", studentsExercise.CurrentReleaseId)),
                CheckingWindowType.Post16);
            Assert.Equal(240, pupils.Count);
            Assert.Equal(120, pupils.Count(p => p.IsIncluded));

            var resultsExercise = october.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
            var seeded = System.Text.Json.JsonSerializer.Deserialize<List<DfE.CheckPerformanceData.Application.ResultsEnquiry.StudentResultRecord>>(
                await ReadAsync(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(resultsExercise.Id, CheckingDataType.Results, "860/4070", resultsExercise.CurrentReleaseId)),
                DfE.CheckPerformanceData.Infrastructure.BlobStorage.StudentResultsBlobClient.JsonOptions)!;
            Assert.Equal(
                SeedStudentResults.All.Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_NONINC"], seeded.Select(r => r.SourceFile).Distinct().Order());

            await using (var ctx = CreateContext())
            {
                var availability = new DfE.CheckPerformanceData.Application.ResultsEnquiry.LateResultsAvailability(
                    new DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseStorageResolver(
                        new WindowRepository(ctx), TimeProvider.System));
                Assert.True(await availability.IsAwaitingSecondLateResultsAsync(_october));
            }
        }
        finally
        {
            await blobs.GetBlobContainerClient(_october.ToString()).DeleteIfExistsAsync();
        }

        async Task<byte[]> ReadAsync(string blobName) =>
            (await blobs.GetBlobContainerClient(_october.ToString()).GetBlobClient(blobName).DownloadContentAsync())
                .Value.Content.ToArray();
    }

    [Fact]
    public async Task Fixture_windows_are_ingested_so_every_exercise_has_a_release_and_journey_data()
    {
        // The KS4 fixture windows are seeded through ingress, like an admin upload, so the Check
        // Your Pupil Data page draws schema-driven tabs for them. The journeys still read the
        // same fixtures: pupils by Id.
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        try
        {
            await using (var ctx = CreateContext())
            {
                var ingress = new CheckingExerciseIngress(
                    new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
                    new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                        new Dictionary<string, BlobServiceClient> { ["app"] = blobs }), TimeProvider.System);
                await SeedExerciseFixtures.ExecuteSeedAsync(ctx, blobs, ingress, AppContext.BaseDirectory,
                    [_openKs4, _closedKs4]);
            }

            foreach (var windowId in new[] { _openKs4, _closedKs4 })
            {
                var window = await LoadAsync(windowId);
                Assert.All(window.CheckingExercises, e =>
                {
                    Assert.True(e.IsEnabled, $"{e.Name} is not enabled");
                    Assert.False(string.IsNullOrWhiteSpace(e.TabName), $"{e.Name} has no tab");
                    Assert.NotNull(e.CurrentReleaseId);
                    Assert.NotNull(e.Validated);
                    Assert.All(e.Datasets, d => Assert.False(string.IsNullOrEmpty(d.SchemaFile)));
                });
            }

            // KS4: Alice Smith, whom the E2E suite drives by UPN, keeps a real Id.
            var ks4 = (await LoadAsync(_openKs4)).CheckingExercises.Single();
            var ks4Pupils = DfE.CheckPerformanceData.Infrastructure.BlobStorage.PupilDataBlobClient.Deserialize(
                await ReadAsync(_openKs4, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(ks4.Id, CheckingDataType.Pupil, "860/4070", ks4.CurrentReleaseId)),
                CheckingWindowType.KS4June);
            var alice = Assert.Single(ks4Pupils, p => p.Identifier == "A86040700001B");
            Assert.Equal(("Alice", "Smith"), (alice.Firstname, alice.Surname));
            Assert.NotEqual(Guid.Empty, alice.Id);
            Assert.Equal(240, ks4Pupils.Count);
        }
        finally
        {
            foreach (var windowId in new[] { _openKs4, _closedKs4 })
                await blobs.GetBlobContainerClient(windowId.ToString()).DeleteIfExistsAsync();
        }

        async Task<byte[]> ReadAsync(Guid windowId, string blobName) =>
            (await blobs.GetBlobContainerClient(windowId.ToString()).GetBlobClient(blobName).DownloadContentAsync())
                .Value.Content.ToArray();
    }
}
