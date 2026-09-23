using System.Security.Cryptography;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Web.Seeding;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
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
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry, null],
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
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry, null],
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
        try
        {
            await using (var ctx = CreateContext())
            {
                await SeedPost16Ingress.ExecuteSeedAsync(ctx, blobs, AppContext.BaseDirectory);
                await SeedPost16Ingress.ExecuteSeedAsync(ctx, blobs, AppContext.BaseDirectory);
            }

            var window = await LoadAsync(id);
            Assert.Equal(CheckingWindowType.Post16, window.CheckingWindowType);
            Assert.Equal(KeyStages.Post16, window.KeyStage);
            Assert.Equal(DateTime.Today, window.StartDate);
            Assert.Equal(window.StartDate.AddMonths(1).AddHours(17), window.EndDate);
            Assert.Equal(3, window.CheckingExercises.Count);
            Assert.All(window.CheckingExercises, e =>
            {
                Assert.Equal(window.StartDate, e.StartDate);
                Assert.Equal(window.EndDate, e.EndDate);
            });
            var datasets = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
                .Datasets.OrderBy(d => d.SortOrder).ToList();
            Assert.Equal(new[] { "included", "nonincluded" }, datasets.Select(d => d.Name));
            Assert.Equal(new bool?[] { true, false }, datasets.Select(d => d.Included));
            foreach (var dataset in datasets)
            {
                Assert.Equal($"{dataset.Name}.csv", dataset.IngressFile);
                Assert.Equal($"{dataset.Name}.json", dataset.SchemaFile);
                await AssertFileAsync("ingress", dataset.IngressFile, dataset.IngressFileChecksum, "text/csv");
                await AssertFileAsync("schema", dataset.SchemaFile, dataset.SchemaFileChecksum, "application/json");
            }
            Assert.Equal(datasets[0].IngressFileChecksum, window.IngressFileChecksum);
            Assert.Equal(datasets[0].SchemaFileChecksum, window.SchemaFileChecksum);

            // The display-only Summary share's single slot must be uploaded too, or the loop added
            // for it in SeedPost16Ingress is dead code that only compiles.
            var summaryDataset = Assert.Single(
                window.CheckingExercises.Single(e => e.ExerciseType == null).Datasets);
            Assert.Equal("summary", summaryDataset.Name);
            Assert.Equal("summary.csv", summaryDataset.IngressFile);
            Assert.Equal("summary.json", summaryDataset.SchemaFile);
            Assert.NotEqual(string.Empty, summaryDataset.IngressFileChecksum);
            Assert.NotEqual(string.Empty, summaryDataset.SchemaFileChecksum);
            await AssertFileAsync("ingress", summaryDataset.IngressFile, summaryDataset.IngressFileChecksum, "text/csv");
            await AssertFileAsync("schema", summaryDataset.SchemaFile, summaryDataset.SchemaFileChecksum, "application/json");

            // A results enquiry takes one slot per source file, and the seed fills the two files a
            // supplier has actually sent by the time a window opens. The other three are optional
            // (#324) and stay empty on purpose: that is what proves the exercise validates on its
            // main file alone, and that the seed skips a slot with no file rather than throwing.
            var results = window.CheckingExercises
                .Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry)
                .Datasets.OrderBy(d => d.SortOrder).ToList();
            Assert.Equal(
                new[]
                {
                    ResultsFileTags.Post16Main, ResultsFileTags.Post16LateResults1,
                    ResultsFileTags.Post16LateResults2, ResultsFileTags.Post16Revised,
                    ResultsFileTags.Post16Retention
                },
                results.Select(d => d.Name));
            Assert.Equal(results.Select(d => d.Name), results.Select(d => d.SourceFile));
            Assert.Equal(new[] { true, false, false, false, false }, results.Select(d => d.Required));
            Assert.All(results, d => Assert.Null(d.Included));

            foreach (var dataset in results.Take(2))
            {
                Assert.Equal($"{dataset.Name}.csv", dataset.IngressFile);
                Assert.Equal($"{dataset.Name}.json", dataset.SchemaFile);
                await AssertFileAsync("ingress", dataset.IngressFile, dataset.IngressFileChecksum, "text/csv");
                await AssertFileAsync("schema", dataset.SchemaFile, dataset.SchemaFileChecksum, "application/json");
            }

            Assert.All(results.Skip(2), d =>
            {
                Assert.Equal(string.Empty, d.IngressFile);
                Assert.Equal(string.Empty, d.SchemaFile);
            });
        }
        finally
        {
            await container.DeleteIfExistsAsync();
        }

        async Task AssertFileAsync(string folder, string filename, string checksum, string contentType)
        {
            var original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", folder, filename));
            var downloaded = (await container.GetBlobClient($"{folder}/{filename}").DownloadContentAsync()).Value;
            Assert.True(original.AsSpan().SequenceEqual(downloaded.Content.ToArray()), "Seed blob must match its source file.");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), checksum);
            Assert.Equal(checksum, downloaded.Details.Metadata["sha256"]);
            Assert.Equal(contentType, downloaded.Details.ContentType);
        }
    }

    [Fact]
    public async Task Every_seeded_exercise_is_named()
    {
        await using var ctx = CreateContext();
        var rows = await ctx.CheckingExercises.Select(e => new { e.Name, e.TabName }).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.False(string.IsNullOrWhiteSpace(r.TabName));
        });
    }

    [Fact]
    public async Task The_open_post16_window_has_a_summary_data_share_with_one_slot()
    {
        // This fixture seeds with its own random ids (_post16), not DevDataSeeder's fixed
        // constants — _post16 is what SeedCheckingWindows.ExecuteSeed was given as the Post16
        // window id in InitializeAsync above, i.e. the same seed-time role as
        // DevDataSeeder.Post16CheckingWindowId plays against the real dev database.
        await using var ctx = CreateContext();
        var summary = await ctx.CheckingExercises.Include(e => e.Datasets)
            .SingleAsync(e => e.CheckingWindowId == _post16 && e.ExerciseType == null);
        Assert.Equal("Summary data (Autumn)", summary.Name);
        Assert.Equal("Summary", summary.TabName);
        Assert.Equal(WindowExercises.DisplayOnlySortOrderStart, summary.SortOrder);
        Assert.Equal("summary", Assert.Single(summary.Datasets).Name);

        // "Its dates are its visibility" (SeedCheckingWindows) — assert the seed's own convention
        // rather than leaving the only hand-rolled values in the seed block unchecked.
        Assert.Equal(summary.StartDate.AddMonths(2).Date.AddHours(17), summary.EndDate);
        Assert.True(summary.EndDate > DateTime.Now, "the seeded Summary share must still be open locally");
    }
}
