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
// model together: a window's outer dates are the union of its exercises' dates.
[Collection(nameof(AzuriteCollection))]
public sealed class SeededCheckingExerciseTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly Guid _openKs4 = Guid.NewGuid();
    private readonly Guid _closedKs4 = Guid.NewGuid();
    private readonly Guid _post16 = Guid.NewGuid();
    private readonly Guid _closedPupilDataPost16 = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _post16, _closedPupilDataPost16);
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
    public async Task The_post16_window_runs_pupil_data_and_results_enquiry_on_different_ranges()
    {
        var window = await LoadAsync(_post16);

        var exercises = window.CheckingExercises.OrderBy(e => e.SortOrder).ToList();
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            exercises.Select(e => e.ExerciseType));
        Assert.True(
            exercises[1].EndDate > exercises[0].EndDate,
            "Results enquiry must outlast pupil data checking, or the multi-exercise shape is untestable.");
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
        foreach (var windowId in new[] { _openKs4, _closedKs4, _post16, _closedPupilDataPost16, DevDataSeeder.Post16IngressCheckingWindowId })
        {
            var window = await LoadAsync(windowId);

            Assert.NotEmpty(window.CheckingExercises);
            Assert.Equal(window.StartDate, window.CheckingExercises.Min(e => e.StartDate));
            Assert.Equal(window.EndDate, window.CheckingExercises.Max(e => e.EndDate));
        }
    }

    // AB#298317: the state the ticket is about — pupil data has closed, results enquiry is still
    // open, and the window knows when the school can next review its data. E2E and the local
    // hand-walk rely on this window; the other three are untouched.
    [Fact]
    public async Task The_closed_pupil_data_post16_window_has_pupil_data_shut_and_results_enquiry_open()
    {
        var window = await LoadAsync(_closedPupilDataPost16);
        var now = DateTime.Now;

        var exercises = window.CheckingExercises.OrderBy(e => e.SortOrder).ToList();
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            exercises.Select(e => e.ExerciseType));
        Assert.True(exercises[0].EndDate < now, "pupil data must already have closed");
        Assert.True(exercises[1].StartDate <= now && exercises[1].EndDate > now, "results enquiry must still be open");
        Assert.Equal(new DateTime(now.Year + 1, 10, 1), window.NextOpportunity);
    }

    [Fact]
    public async Task The_open_post16_window_also_carries_a_next_opportunity()
    {
        var window = await LoadAsync(_post16);
        Assert.Equal(new DateTime(DateTime.Now.Year + 1, 10, 1), window.NextOpportunity);
    }
    [Fact]
    public async Task Post16_ingress_seed_links_both_file_pairs_and_can_be_repeated()
    {
        var id = DevDataSeeder.Post16IngressCheckingWindowId;
        Assert.DoesNotContain(id, new[] { DevDataSeeder.Post16CheckingWindowId,
            DevDataSeeder.KeyStage4JuneCheckingWindowId, DevDataSeeder.ClosedKeyStage4JuneCheckingWindowId });
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        var container = blobs.GetBlobContainerClient(id.ToString());
        // Seed from a trimmed copy of the seed files — every schema, and each CSV cut to the dev
        // sign-in school's rows — so the test asserts on one known school and still validates
        // every row the startup seed will ingress for it.
        var contentRoot = TrimmedContentRoot("8604070");
        try
        {
            await using (var ctx = CreateContext())
            {
                var ingress = new CheckingExerciseIngress(
                    new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
                    new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                        new Dictionary<string, BlobServiceClient> { ["app"] = blobs }), TimeProvider.System);
                await SeedPost16Ingress.ExecuteSeedAsync(ctx, blobs, ingress, contentRoot);
                await SeedPost16Ingress.ExecuteSeedAsync(ctx, blobs, ingress, contentRoot);
            }

            var window = await LoadAsync(id);
            Assert.Equal(CheckingWindowType.Post16, window.CheckingWindowType);
            Assert.Equal(KeyStages.Post16, window.KeyStage);
            Assert.Equal(DateTime.Today, window.StartDate);
            Assert.Equal(window.StartDate.AddMonths(1).AddHours(17), window.EndDate);
            Assert.Equal(6, window.CheckingExercises.Count);
            Assert.All(window.CheckingExercises, e =>
            {
                Assert.Equal(window.StartDate, e.StartDate);
                Assert.Equal(window.EndDate, e.EndDate);
            });
            var tabs = window.CheckingExercises.Where(e => !e.DisplayOnly).OrderBy(e => e.TabOrder).ToList();
            Assert.Equal(new[] { "Students", "Results" }, tabs.Select(e => e.TabName));
            Assert.Equal(new[] { 200, 300 }, tabs.Select(e => e.TabOrder));
            Assert.Equal(new[] { 1, 2 }, tabs.Select(e => e.SortOrder));
            Assert.All(tabs, e => Assert.True(e.IsEnabled));

            // The Summary tab is four exercises over the year, one visible at a time: each replaces
            // the one before, and the supplier's file has a different column set for each, so each
            // dataset carries the schema for its own shape. Only the first is enabled, but all four
            // are linked and ingressed so an admin can swap the tab by enabling the next.
            var summaries = SummaryChain(window.CheckingExercises);
            Assert.Equal(
                new[] { "Autumn CE", "Provisional value added", "Light touch revised data share", "Retention" },
                summaries.Select(e => e.Name));
            Assert.Equal(new[] { true, false, false, false }, summaries.Select(e => e.IsEnabled));
            Assert.All(summaries, e =>
            {
                Assert.Equal("Summary", e.TabName);
                Assert.Equal(100, e.TabOrder);
                Assert.Equal(0, e.SortOrder);
                Assert.Null(e.ExerciseType);
                Assert.True(e.DisplayOnly);
                Assert.True(e.UsesExerciseStorage);
                Assert.NotNull(e.Validated);
            });
            var summaryDatasets = summaries.Select(e => Assert.Single(e.Datasets)).ToList();
            Assert.Equal(
                new[] { "summary-autumn", "summary-november-va", "summary-november-va", "summary-retention" },
                summaryDatasets.Select(d => d.Name));
            foreach (var (exercise, dataset) in summaries.Zip(summaryDatasets))
            {
                Assert.True(dataset.Required);
                Assert.Null(dataset.Included);
                Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, $"{dataset.Name}.csv"), dataset.IngressFile);
                Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, $"{dataset.Name}_schema.json"), dataset.SchemaFile);
                await AssertFileAsync(dataset.IngressFile, dataset.IngressFileChecksum, "text/csv");
                await AssertFileAsync(dataset.SchemaFile, dataset.SchemaFileChecksum, "application/json");
                // A display-only share has no journey, so its release writes only each dataset's own file.
                Assert.True(await container.GetBlobClient(
                    DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DatasetBlobName(
                        exercise.Id, exercise.CurrentReleaseId!.Value, dataset.Id, "8604070")).ExistsAsync(), $"{exercise.Name} output missing");
            }
            Assert.Equal(CheckingExerciseType.PupilData, tabs[0].ExerciseType);
            Assert.Equal(CheckingExerciseType.ResultsEnquiry, tabs[1].ExerciseType);
            var datasets = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
                .Datasets.OrderBy(d => d.SortOrder).ToList();
            Assert.Equal(new[] { "included", "nonincluded" }, datasets.Select(d => d.Name));
            Assert.Equal(new bool?[] { true, false }, datasets.Select(d => d.Included));
            foreach (var dataset in datasets)
            {
                Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(dataset.CheckingExerciseId, dataset.Id, $"{dataset.Name}.csv"), dataset.IngressFile);
                var schemaName = dataset.Name == "included" ? "students-included_schema.json" : "students-non-included_schema.json";
                Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(dataset.CheckingExerciseId, dataset.Id, schemaName), dataset.SchemaFile);
                await AssertFileAsync(dataset.IngressFile, dataset.IngressFileChecksum, "text/csv");
                await AssertFileAsync(dataset.SchemaFile, dataset.SchemaFileChecksum, "application/json");
            }
            // Staged as the start of the Autumn window: the results enquiry holds the supplier's
            // main results file, in the workbook's shape, tagged 16to19_MAIN by its slot.
            var results = Assert.Single(tabs[1].Datasets);
            Assert.Equal(DfE.CheckPerformanceData.Application.ResultsEnquiry.ResultsFileTags.Post16Main, results.Name);
            Assert.Equal(DfE.CheckPerformanceData.Application.ResultsEnquiry.ResultsFileTags.Post16Main, results.SourceFile);
            Assert.True(results.Required);
            Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(tabs[1].Id, results.Id, "16to19_MAIN.csv"), results.IngressFile);
            Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(tabs[1].Id, results.Id, "results-included_schema.json"), results.SchemaFile);
            await AssertFileAsync(results.IngressFile, results.IngressFileChecksum, "text/csv");
            await AssertFileAsync(results.SchemaFile, results.SchemaFileChecksum, "application/json");
            Assert.True(await container.GetBlobClient(
                DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DataBlobName(
                        tabs[1].Id, CheckingDataType.Results, "8604070", tabs[1].CurrentReleaseId)).ExistsAsync(), "Results output missing");
            Assert.NotNull(tabs[1].Validated);
            // The seed runs the same ingress as the admin button, so each run is a live release.
            Assert.NotNull(tabs[1].CurrentReleaseId);
            Assert.Equal(datasets[0].IngressFileChecksum, window.IngressFileChecksum);
            Assert.Equal(datasets[0].SchemaFileChecksum, window.SchemaFileChecksum);

            // The seed runs each exercise's ingress, so the landing page's HasPupilData check and
            // every tab have per-school output from first boot — not only after an admin clicks
            // Validate. 8604070 is the dev sign-in school (860/4070).
            Assert.True(await container.GetBlobClient(
                DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DataBlobName(
                        tabs[0].Id, CheckingDataType.Pupil, "8604070", tabs[0].CurrentReleaseId)).ExistsAsync(), "Students output missing");
            Assert.NotNull(tabs[0].Validated);
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            Directory.Delete(contentRoot, recursive: true);
        }

        async Task AssertFileAsync(string filename, string checksum, string contentType)
        {
            var original = await File.ReadAllBytesAsync(Path.Combine(contentRoot, "Data", "Ingress", "post16", Path.GetFileName(filename)));
            var downloaded = (await container.GetBlobClient(filename).DownloadContentAsync()).Value;
            Assert.True(original.AsSpan().SequenceEqual(downloaded.Content.ToArray()), "Seed blob must match its source file.");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), checksum);
            Assert.Equal(checksum, downloaded.Details.Metadata["sha256"]);
            Assert.Equal(contentType, downloaded.Details.ContentType);
        }
    }

    [Fact]
    public async Task February_window_is_staged_mid_year_with_pupil_data_shut_and_late_results()
    {
        var id = DevDataSeeder.Post16FebruaryCheckingWindowId;
        Assert.NotEqual(DevDataSeeder.Post16IngressCheckingWindowId, id);
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        var container = blobs.GetBlobContainerClient(id.ToString());
        var contentRoot = TrimmedContentRoot("8604070");
        try
        {
            await using (var ctx = CreateContext())
            {
                var ingress = new CheckingExerciseIngress(
                    new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
                    new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                        new Dictionary<string, BlobServiceClient> { ["app"] = blobs }), TimeProvider.System);
                await SeedPost16Ingress.ExecuteSeedAsync(ctx, blobs, ingress, contentRoot);
            }

            var window = await LoadAsync(id);
            Assert.Equal(CheckingWindowType.Post16, window.CheckingWindowType);
            Assert.Equal("16 to 19 February", window.Title);
            // The window opened in the autumn and runs on past today: results enquiry is open,
            // pupil data checking shut a fortnight after the start, so the Students tab shows data
            // with no request or confirm actions.
            Assert.True(window.StartDate < DateTime.Today.AddMonths(-3));
            Assert.True(window.EndDate > DateTime.Today.AddMonths(1));
            var students = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
            Assert.Equal(window.StartDate, students.StartDate);
            Assert.True(students.EndDate < DateTime.Today);
            Assert.Equal(window.EndDate, results.EndDate);
            Assert.True(await container.GetBlobClient(
                DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DataBlobName(
                        students.Id, CheckingDataType.Pupil, "8604070", students.CurrentReleaseId)).ExistsAsync(), "Students output missing");

            // The February summary is the third exercise in the chain; the two before it have run.
            var summaries = SummaryChain(window.CheckingExercises);
            Assert.Equal(new[] { false, false, true, false }, summaries.Select(e => e.IsEnabled));
            Assert.Equal("Light touch revised data share", summaries[2].Name);
            Assert.Equal("summary-november-va", Assert.Single(summaries[2].Datasets).Name);
            Assert.True(await container.GetBlobClient(
                DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DatasetBlobName(
                        summaries[2].Id, summaries[2].CurrentReleaseId!.Value, summaries[2].Datasets.Single().Id, "8604070")).ExistsAsync(), "Summary output missing");

            // By February the main and first late results files have both landed.
            var slots = results.Datasets.OrderBy(d => d.SortOrder).ToList();
            Assert.Equal(
                new[] { DfE.CheckPerformanceData.Application.ResultsEnquiry.ResultsFileTags.Post16Main,
                        DfE.CheckPerformanceData.Application.ResultsEnquiry.ResultsFileTags.Post16LateResults1 },
                slots.Select(d => d.Name));
            Assert.Equal(slots.Select(d => d.Name), slots.Select(d => d.SourceFile));
            Assert.Equal(new[] { true, false }, slots.Select(d => d.Required));
            foreach (var slot in slots)
            {
                Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(results.Id, slot.Id, $"{slot.Name}.csv"), slot.IngressFile);
                Assert.Equal(DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DefinitionFile(results.Id, slot.Id, "results-included_schema.json"), slot.SchemaFile);
            }
            var output = (await container.GetBlobClient(
                DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths.DataBlobName(
                        results.Id, CheckingDataType.Results, "8604070", results.CurrentReleaseId)).DownloadContentAsync()).Value.Content.ToString();
            Assert.Contains("\"16to19_MAIN\"", output);
            Assert.Contains("\"16to19_LR1\"", output);
            Assert.NotNull(results.Validated);
            Assert.NotNull(results.CurrentReleaseId);
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Window_seed_can_be_repeated_over_a_replacement_chain()
    {
        // The dev seed deletes every window on each start. The Summary exercises reference each
        // other through ReplacesCheckingExerciseId, whose FK is ON DELETE RESTRICT, so the cascade
        // from the window must not trip over the chain.
        for (var start = 0; start < 2; start++)
        {
            await using var ctx = CreateContext();
            await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _post16, _closedPupilDataPost16);
        }

        var window = await LoadAsync(DevDataSeeder.Post16IngressCheckingWindowId);
        Assert.Equal(4, SummaryChain(window.CheckingExercises).Count);
    }

    /// <summary>The Summary exercises in replacement order: the root first, then whichever names it.</summary>
    private static List<CheckingExercise> SummaryChain(IEnumerable<CheckingExercise> exercises)
    {
        var summaries = exercises.Where(e => e.DisplayOnly && e.TabName == "Summary").ToList();
        var chain = new List<CheckingExercise> { Assert.Single(summaries, e => e.ReplacesCheckingExerciseId is null) };
        while (summaries.SingleOrDefault(e => e.ReplacesCheckingExerciseId == chain[^1].Id) is { } next) chain.Add(next);
        Assert.Equal(summaries.Count, chain.Count);
        return chain;
    }

    [Fact]
    public async Task Fixture_windows_are_ingested_so_every_exercise_has_a_release_and_journey_data()
    {
        // The E2E fixture windows are seeded through ingress, like an admin upload, so the Check
        // Your Pupil Data page draws schema-driven tabs for them. The journeys still read the
        // same fixtures: pupils by Id, results by QAN and session.
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
                    [_openKs4, _closedKs4], [_post16, _closedPupilDataPost16]);
            }

            foreach (var windowId in new[] { _openKs4, _closedKs4, _post16, _closedPupilDataPost16 })
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

            // 16-19: both populations in the journey file, each with an Id and its inclusion stamp.
            var post16 = await LoadAsync(_post16);
            var students = post16.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            var post16Pupils = DfE.CheckPerformanceData.Infrastructure.BlobStorage.PupilDataBlobClient.Deserialize(
                await ReadAsync(_post16, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(students.Id, CheckingDataType.Pupil, "860/4070", students.CurrentReleaseId)),
                CheckingWindowType.Post16);
            Assert.Equal(240, post16Pupils.Count);
            Assert.Equal(120, post16Pupils.Count(p => p.IsIncluded));
            Assert.DoesNotContain(post16Pupils, p => p.Id == Guid.Empty);

            // Results keep the fields the enquiry journey reads, and the tag of the file they came in.
            var results = post16.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
            var seeded = System.Text.Json.JsonSerializer.Deserialize<List<DfE.CheckPerformanceData.Application.ResultsEnquiry.StudentResultRecord>>(
                await ReadAsync(_post16, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(results.Id, CheckingDataType.Results, "860/4070", results.CurrentReleaseId)),
                DfE.CheckPerformanceData.Infrastructure.BlobStorage.StudentResultsBlobClient.JsonOptions)!;
            Assert.Equal(SeedStudentResults.All.Count, seeded.Count);
            Assert.Equal(
                SeedStudentResults.All.Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
        }
        finally
        {
            foreach (var windowId in new[] { _openKs4, _closedKs4, _post16, _closedPupilDataPost16 })
                await blobs.GetBlobContainerClient(windowId.ToString()).DeleteIfExistsAsync();
        }

        async Task<byte[]> ReadAsync(Guid windowId, string blobName) =>
            (await blobs.GetBlobContainerClient(windowId.ToString()).GetBlobClient(blobName).DownloadContentAsync())
                .Value.Content.ToArray();
    }

    private static string TrimmedContentRoot(string laestab)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "post16");
        var root = Path.Combine(Path.GetTempPath(), "cpd-seed-" + Guid.NewGuid().ToString("N"));
        var post16Dir = Path.Combine(root, "Data", "Ingress", "post16");
        Directory.CreateDirectory(post16Dir);
        foreach (var schema in Directory.GetFiles(source, "*.json"))
            File.Copy(schema, Path.Combine(post16Dir, Path.GetFileName(schema)));
        foreach (var csv in Directory.GetFiles(source, "*.csv"))
        {
            var lines = File.ReadAllLines(csv);
            var laestabColumn = Array.IndexOf(lines[0].Split(','), "LAESTAB");
            var rows = lines.Skip(1).Where(l => l.Split(',')[laestabColumn] == laestab);
            File.WriteAllLines(Path.Combine(post16Dir, Path.GetFileName(csv)), [lines[0], .. rows]);
        }
        return root;
    }
}
