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
// model together: a window's outer dates are the union of its exercises' dates. The four 16-19
// windows are one step of the results enquiry year each ("16 to 19 Oct", "Nov", "Feb" and "Mar"),
// and each Web seed does the steps before its own; the KS4 windows are fixtures, ingested by the seed.
[Collection(nameof(AzuriteCollection))]
public sealed class SeededCheckingExerciseTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly Guid _openKs4 = Guid.NewGuid();
    private readonly Guid _closedKs4 = Guid.NewGuid();
    private readonly Guid _october = Guid.NewGuid();
    private readonly Guid _november = Guid.NewGuid();
    private readonly Guid _february = Guid.NewGuid();
    private readonly Guid _march = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _october, _november, _february, _march);
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
    public async Task The_window_seed_gives_every_slot_but_no_data()
    {
        // The Web seed imports the files: the window seed alone links nothing and runs nothing.
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
        foreach (var windowId in new[] { _openKs4, _closedKs4, _october, _november, _february, _march })
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
            await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _october, _november, _february, _march);
        }

        Assert.Equal(2, (await LoadAsync(_october)).CheckingExercises.Count);
    }

    [Fact]
    public async Task The_October_window_has_the_October_files_validated()
    {
        // The seed does what the admin does by hand: choose each sample CSV and schema for its slot,
        // then validate. The journey then finds the students and every October result, each with
        // the tag of the file it came in, and the second late results file is still awaited.
        await SeedAsync(_october, SeedPost16OctoberSamples.ExecuteSeedAsync);
        try
        {
            var october = await LoadAsync(_october);
            Assert.All(october.CheckingExercises, e =>
            {
                Assert.NotNull(e.CurrentReleaseId);
                Assert.NotNull(e.Validated);
            });

            var studentsExercise = october.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            var pupils = DfE.CheckPerformanceData.Infrastructure.BlobStorage.PupilDataBlobClient.Deserialize(
                await ReadAsync(_october, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(studentsExercise.Id, CheckingDataType.Pupil, "860/4070", studentsExercise.CurrentReleaseId)),
                CheckingWindowType.Post16);
            Assert.Equal(240, pupils.Count);
            Assert.Equal(120, pupils.Count(p => p.IsIncluded));

            var results = Results(october);
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_NONINC"], Linked(results));
            Assert.Equal(1, await ReleaseCountAsync(results));
            var seeded = await ResultsAsync(_october, results);
            Assert.Equal(
                SeedStudentResults.All.Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_NONINC"], seeded.Select(r => r.SourceFile).Distinct().Order());
            Assert.True(await AwaitingSecondLateResultsAsync(_october));
        }
        finally
        {
            await DeleteBlobsAsync(_october);
        }
    }

    [Fact]
    public async Task The_November_window_has_late_results_2_validated_after_the_October_files()
    {
        // The October import, then late results 2 and a second run: a new release that holds it.
        await SeedAsync(_november, SeedPost16NovemberSamples.ExecuteSeedAsync);
        try
        {
            var november = await LoadAsync(_november);
            Assert.Equal("16 to 19 Nov", november.Title);
            var results = Results(november);
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_LR2", "16to19_NONINC"], Linked(results));
            Assert.DoesNotContain(results.Datasets, d => d.Retired);
            Assert.Equal(2, await ReleaseCountAsync(results));

            var seeded = await ResultsAsync(_november, results);
            Assert.Equal(
                SeedStudentResults.All.Concat(SeedStudentResults.LateResults2).Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            // A student who held nothing in October now has a result.
            Assert.Contains(seeded, r => r.CypmdId == "500005" && r.SourceFile == "16to19_LR2");
            Assert.False(await AwaitingSecondLateResultsAsync(_november));
        }
        finally
        {
            await DeleteBlobsAsync(_november);
        }
    }

    [Fact]
    public async Task The_February_window_has_the_revised_files_validated_and_the_four_they_replace_retired()
    {
        // The November steps, then the revised files: the release holds only the revised rows.
        await SeedAsync(_february, SeedPost16FebruarySamples.ExecuteSeedAsync);
        try
        {
            var february = await LoadAsync(_february);
            Assert.Equal("16 to 19 Feb", february.Title);
            var results = Results(february);
            Assert.Equal(
                ["16to19_INC", "16to19_INC_REV", "16to19_LR1", "16to19_LR2", "16to19_NONINC", "16to19_NONINC_REV"],
                Linked(results));
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_LR2", "16to19_NONINC"],
                results.Datasets.Where(d => d.Retired).Select(d => d.Name).Order());
            Assert.Equal(3, await ReleaseCountAsync(results));

            var revised = await ResultsAsync(_february, results);
            Assert.Equal(SeedStudentResults.Revised.Select(r => r.CompositeKey).Order(), revised.Select(r => r.CompositeKey).Order());
            // The amendment replaced the row it corrected: one English row for Alice, at grade 7.
            Assert.Equal("7", Assert.Single(revised, r => r.CypmdId == "500001" && r.Qan == "60148366").Grade);
            Assert.False(await AwaitingSecondLateResultsAsync(_february));
        }
        finally
        {
            await DeleteBlobsAsync(_february);
        }
    }

    [Fact]
    public async Task The_March_window_has_included_revised_with_retention_validated_and_included_revised_retired()
    {
        // The February steps, then included revised with retention replaces included revised. The
        // non-included revised file stays in use.
        await SeedAsync(_march, SeedPost16MarchSamples.ExecuteSeedAsync);
        try
        {
            var march = await LoadAsync(_march);
            Assert.Equal("16 to 19 Mar", march.Title);
            var results = Results(march);
            Assert.Equal(["16to19_INC", "16to19_INC_REV", "16to19_LR1", "16to19_LR2", "16to19_NONINC"],
                results.Datasets.Where(d => d.Retired).Select(d => d.Name).Order());
            Assert.Equal(["16to19_INC_REV_RET", "16to19_NONINC_REV"],
                results.Datasets.Where(d => !d.Retired && d.IngressFile != string.Empty).Select(d => d.Name).Order());
            Assert.Equal(4, await ReleaseCountAsync(results));

            var seeded = await ResultsAsync(_march, results);
            Assert.Equal(
                SeedStudentResults.IncludedRevisedWithRetention
                    .Concat(SeedStudentResults.Revised.Where(r => r.SourceFile == "16to19_NONINC_REV"))
                    .Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            Assert.Equal(["16to19_INC_REV_RET", "16to19_NONINC_REV"], seeded.Select(r => r.SourceFile).Distinct().Order());
            // The grade only the retention file changes: Charlie Smith's Maths, 3 → 4.
            Assert.Equal("4", Assert.Single(seeded, r => r.CypmdId == "500003" && r.Qan == "60146084" && r.Session == "S2024").Grade);
        }
        finally
        {
            await DeleteBlobsAsync(_march);
        }
    }

    private async Task SeedAsync(Guid windowId,
        Func<IPortalDbContext, BlobServiceClient, ICheckingExerciseIngress, string, Guid, Task> seed)
    {
        await using var ctx = CreateContext();
        await seed(ctx, new BlobServiceClient(azurite.ConnectionString), Ingress(ctx), AppContext.BaseDirectory, windowId);
    }

    private static CheckingExercise Results(CheckingWindow window) =>
        window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);

    private static IEnumerable<string> Linked(CheckingExercise exercise) =>
        exercise.Datasets.Where(d => d.IngressFile != string.Empty).Select(d => d.Name).Order();

    private async Task<int> ReleaseCountAsync(CheckingExercise exercise)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<CheckingExerciseRelease>().CountAsync(r => r.CheckingExerciseId == exercise.Id);
    }

    private async Task<byte[]> ReadAsync(Guid windowId, string blobName) =>
        (await new BlobServiceClient(azurite.ConnectionString).GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(blobName).DownloadContentAsync()).Value.Content.ToArray();

    private async Task<List<DfE.CheckPerformanceData.Application.ResultsEnquiry.StudentResultRecord>> ResultsAsync(
        Guid windowId, CheckingExercise exercise) =>
        System.Text.Json.JsonSerializer.Deserialize<List<DfE.CheckPerformanceData.Application.ResultsEnquiry.StudentResultRecord>>(
            await ReadAsync(windowId, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                .DataBlobName(exercise.Id, CheckingDataType.Results, "860/4070", exercise.CurrentReleaseId)),
            DfE.CheckPerformanceData.Infrastructure.BlobStorage.StudentResultsBlobClient.JsonOptions)!;

    private async Task<bool> AwaitingSecondLateResultsAsync(Guid windowId)
    {
        await using var ctx = CreateContext();
        return await new DfE.CheckPerformanceData.Application.ResultsEnquiry.LateResultsAvailability(
            new DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseStorageResolver(
                new WindowRepository(ctx), TimeProvider.System)).IsAwaitingSecondLateResultsAsync(windowId);
    }

    private Task DeleteBlobsAsync(Guid windowId) =>
        new BlobServiceClient(azurite.ConnectionString).GetBlobContainerClient(windowId.ToString()).DeleteIfExistsAsync();

    private CheckingExerciseIngress Ingress(PortalDbContext ctx) =>
        new(new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
            new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                new Dictionary<string, BlobServiceClient> { ["app"] = new BlobServiceClient(azurite.ConnectionString) }),
            TimeProvider.System);

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
