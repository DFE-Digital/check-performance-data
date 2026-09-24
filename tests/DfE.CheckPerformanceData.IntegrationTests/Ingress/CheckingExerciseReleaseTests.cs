using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

/// <summary>
/// A 16-19 results enquiry gets its data as full replacements: the first set, then a revised set,
/// then another. Each clean run is a release with its own output. Schools see the live release,
/// the earlier ones stay in storage, and an admin can make an earlier one live again. These tests
/// drive the real ingress, repositories and results reader against Postgres and Azurite.
/// </summary>
[Collection(nameof(AzuriteCollection))]
public sealed class CheckingExerciseReleaseTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private const string Laestab = "860/4070";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);
    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private readonly Guid _includedId = Guid.NewGuid();
    private readonly Guid _nonIncludedId = Guid.NewGuid();

    // One cache for the whole test, as in the running app: a new release must not be hidden behind
    // the reader's 30-minute cache.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private const string Schema = """
    {
      "type": "object",
      "properties": {
        "CYPMD_ID":  { "type": ["string", "null"] },
        "QAN":       { "type": ["string", "null"] },
        "SESSION":   { "type": ["string", "null"] },
        "GRADE":     { "type": ["string", "null"] },
        "SOURCE":    { "type": "string" },
        "INCLUDED":  { "type": "boolean" }
      }
    }
    """;

    private const string Header = "CYPMD_ID,QAN,SESSION,GRADE,LAESTAB\n";

    private static string Csv(string grade) => Header + $"500001,60181576,S2024,{grade},8604070\n";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = _windowId,
            Title = "16 to 19",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = _exerciseId,
                    ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    TabName = "Results",
                    IsEnabled = true,
                    UsesExerciseStorage = true,
                    StartDate = new DateTime(2026, 1, 1),
                    EndDate = new DateTime(2026, 12, 31),
                    Datasets =
                    [
                        new CheckingWindowDataset
                        {
                            Id = _includedId, CheckingWindowId = _windowId, Name = "included", Included = true,
                            SourceFile = ResultsFileTags.Post16Main, FeedsJourney = true, SortOrder = 0
                        },
                        new CheckingWindowDataset
                        {
                            Id = _nonIncludedId, CheckingWindowId = _windowId, Name = "nonincluded", Included = false,
                            SourceFile = ResultsFileTags.Post16Main, FeedsJourney = true, SortOrder = 1
                        }
                    ]
                }
            ]
        });
        await ctx.SaveChangesAsync();
        await _blobs.GetBlobContainerClient(_windowId.ToString()).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
    {
        await _blobs.GetBlobContainerClient(_windowId.ToString()).DeleteIfExistsAsync();
        await _postgres.DisposeAsync();
        _cache.Dispose();
    }

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options, new FakeCurrentUserService());

    private static string Checksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// What the admin screens do: upload a file into each slot, under a path that includes the
    /// file's checksum, and point the slot at it. Both slots are replaced together.
    /// </summary>
    private async Task UploadAsync(string includedCsv, string nonIncludedCsv)
    {
        var container = _blobs.GetBlobContainerClient(_windowId.ToString());
        await using var ctx = CreateContext();
        foreach (var (datasetId, csv) in new[] { (_includedId, includedCsv), (_nonIncludedId, nonIncludedCsv) })
        {
            var csvPath = CheckingExerciseBlobPaths.DefinitionFile(_exerciseId, datasetId, Checksum(csv), "results.csv");
            var schemaPath = CheckingExerciseBlobPaths.DefinitionFile(_exerciseId, datasetId, Checksum(Schema), "results.json");
            await container.GetBlobClient(csvPath).UploadAsync(BinaryData.FromString(csv), overwrite: true);
            await container.GetBlobClient(schemaPath).UploadAsync(BinaryData.FromString(Schema), overwrite: true);

            var dataset = await ctx.CheckingWindowDatasets.SingleAsync(d => d.Id == datasetId);
            dataset.IngressFile = csvPath;
            dataset.IngressFileChecksum = Checksum(csv);
            dataset.SchemaFile = schemaPath;
            dataset.SchemaFileChecksum = Checksum(Schema);
        }
        await ctx.SaveChangesAsync();
    }

    private async Task<ValidationProgress> RunAsync()
    {
        await using var ctx = CreateContext();
        var ingress = new CheckingExerciseIngress(
            new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
            new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                new Dictionary<string, BlobServiceClient> { ["app"] = _blobs }),
            TimeProvider.System);

        ValidationProgress? last = null;
        await foreach (var progress in ingress.ProcessAsync(_exerciseId, publishedBy: "admin@example.gov.uk"))
            last = progress;
        return last!;
    }

    /// <summary>What the enquiry journey sees, through the same reader and resolver it uses.</summary>
    private async Task<IReadOnlyList<string>> GradesSchoolsSeeAsync()
    {
        await using var ctx = CreateContext();
        var client = new StudentResultsBlobClient(_blobs, _cache,
            new CheckingExerciseStorageResolver(new WindowRepository(ctx), TimeProvider.System));
        var results = await client.GetResultsAsync(_windowId, Laestab, "500001");
        return [.. results.Select(r => r.Grade).Order()];
    }

    private async Task<CheckingExercise> LoadExerciseAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.CheckingExercises.AsNoTracking()
            .Include(e => e.Releases).ThenInclude(r => r.Files)
            .SingleAsync(e => e.Id == _exerciseId);
    }

    [Fact]
    public async Task A_full_replacement_goes_live_and_the_earlier_release_is_kept()
    {
        await UploadAsync(Csv("5"), Csv("4"));
        Assert.False((await RunAsync()).IsError);
        Assert.Equal(["4", "5"], await GradesSchoolsSeeAsync());

        // The revised files replace both slots together.
        await UploadAsync(Csv("7"), Csv("6"));
        Assert.False((await RunAsync()).IsError);

        // Schools see the revised data at once — not the cached first release.
        Assert.Equal(["6", "7"], await GradesSchoolsSeeAsync());

        var exercise = await LoadExerciseAsync();
        var releases = exercise.Releases.OrderBy(r => r.Number).ToList();
        Assert.Equal([1, 2], releases.Select(r => r.Number));
        Assert.Equal(releases[1].Id, exercise.CurrentReleaseId);
        Assert.All(releases, r => Assert.Equal("admin@example.gov.uk", r.PublishedBy));
        Assert.NotNull(exercise.Validated);

        // The first release's output and its input files are still in storage.
        var container = _blobs.GetBlobContainerClient(_windowId.ToString());
        Assert.True(await container.GetBlobClient(CheckingExerciseBlobPaths.DataBlobName(
            _exerciseId, CheckingDataType.Results, "8604070", releases[0].Id)).ExistsAsync());
        foreach (var file in releases[0].Files)
        {
            Assert.True(await container.GetBlobClient(file.IngressFile).ExistsAsync(), file.IngressFile);
            Assert.True(await container.GetBlobClient(file.SchemaFile).ExistsAsync(), file.SchemaFile);
        }

        // Each release records the files it read, so the first still names the first files.
        Assert.Equal(
            [Checksum(Csv("5")), Checksum(Csv("4"))],
            releases[0].Files.OrderBy(f => f.SortOrder).Select(f => f.IngressFileChecksum));
        Assert.Equal(
            [Checksum(Csv("7")), Checksum(Csv("6"))],
            releases[1].Files.OrderBy(f => f.SortOrder).Select(f => f.IngressFileChecksum));
    }

    [Fact]
    public async Task An_earlier_release_can_be_made_live_again_without_a_rerun()
    {
        await UploadAsync(Csv("5"), Csv("4"));
        await RunAsync();
        await UploadAsync(Csv("7"), Csv("6"));
        await RunAsync();
        var first = (await LoadExerciseAsync()).Releases.Single(r => r.Number == 1);

        await using (var ctx = CreateContext())
        {
            var service = new CheckingExerciseReleaseService(
                new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)));
            Assert.Equal(MakeReleaseLiveResult.MadeLive,
                await service.MakeLiveAsync(_windowId, _exerciseId, first.Id, default));
        }

        Assert.Equal(["4", "5"], await GradesSchoolsSeeAsync());
        Assert.Equal(first.Id, (await LoadExerciseAsync()).CurrentReleaseId);
    }

    [Fact]
    public async Task A_run_that_fails_publishes_nothing_and_schools_keep_the_live_release()
    {
        await UploadAsync(Csv("5"), Csv("4"));
        await RunAsync();
        var live = (await LoadExerciseAsync()).CurrentReleaseId;

        // No LAESTAB column: the run cannot split the file by school and fails by name.
        await UploadAsync("CYPMD_ID,QAN,SESSION,GRADE\n500001,60181576,S2024,9\n", Csv("6"));
        Assert.True((await RunAsync()).IsError);

        var exercise = await LoadExerciseAsync();
        Assert.Single(exercise.Releases);
        Assert.Equal(live, exercise.CurrentReleaseId);
        Assert.Equal(["4", "5"], await GradesSchoolsSeeAsync());
    }

    [Fact]
    public async Task A_release_of_another_exercise_cannot_be_made_current()
    {
        await UploadAsync(Csv("5"), Csv("4"));
        await RunAsync();

        await using var ctx = CreateContext();
        var repository = new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx));

        Assert.False(await repository.SetCurrentReleaseAsync(_exerciseId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task The_window_read_paths_carry_the_releases()
    {
        await UploadAsync(Csv("5"), Csv("4"));
        await RunAsync();

        await using var ctx = CreateContext();
        var window = await new WindowRepository(ctx).GetByIdAsync(_windowId, default);
        var exercise = Assert.Single(window!.Exercises);

        var release = Assert.Single(exercise.Releases);
        Assert.Equal(release.Id, exercise.CurrentReleaseId);
        Assert.Same(release, exercise.CurrentRelease);
        Assert.Equal(["included", "nonincluded"], exercise.PublishedDatasets.Select(d => d.Name));
        Assert.Equal([true, false], exercise.PublishedDatasets.Select(d => d.Included));
    }

    [Fact]
    public async Task A_slot_an_admin_adds_is_shown_but_never_reaches_the_journey()
    {
        // A data share with an unrelated schema, added to the results exercise after it was
        // created. It gets its own file for the tab. The enquiry journey's file holds only the
        // supplier slots, so the share's rows can never become results a school can query.
        const string shareSchema = """
        { "type": "object", "properties": { "CYPMD_ID": { "type": ["string", "null"] }, "NOTE": { "type": ["string", "null"] } } }
        """;
        const string shareCsv = "CYPMD_ID,NOTE,LAESTAB\n500001,Not a result,8604070\n";
        var shareId = Guid.NewGuid();
        var container = _blobs.GetBlobContainerClient(_windowId.ToString());
        var csvPath = CheckingExerciseBlobPaths.DefinitionFile(_exerciseId, shareId, Checksum(shareCsv), "share.csv");
        var schemaPath = CheckingExerciseBlobPaths.DefinitionFile(_exerciseId, shareId, Checksum(shareSchema), "share.json");
        await container.GetBlobClient(csvPath).UploadAsync(BinaryData.FromString(shareCsv), overwrite: true);
        await container.GetBlobClient(schemaPath).UploadAsync(BinaryData.FromString(shareSchema), overwrite: true);
        await using (var ctx = CreateContext())
        {
            ctx.CheckingWindowDatasets.Add(new CheckingWindowDataset
            {
                Id = shareId, CheckingExerciseId = _exerciseId, CheckingWindowId = _windowId, Name = "share",
                Required = false, FeedsJourney = false, SortOrder = 2,
                IngressFile = csvPath, IngressFileChecksum = Checksum(shareCsv),
                SchemaFile = schemaPath, SchemaFileChecksum = Checksum(shareSchema)
            });
            await ctx.SaveChangesAsync();
        }
        await UploadAsync(Csv("5"), Csv("4"));

        Assert.False((await RunAsync()).IsError);

        // The journey sees the two results and nothing from the share.
        Assert.Equal(["4", "5"], await GradesSchoolsSeeAsync());
        var release = (await LoadExerciseAsync()).CurrentReleaseId!.Value;
        var merged = (await container.GetBlobClient(CheckingExerciseBlobPaths.DataBlobName(
            _exerciseId, CheckingDataType.Results, "8604070", release)).DownloadContentAsync()).Value.Content.ToString();
        Assert.DoesNotContain("Not a result", merged);

        // Each slot has its own file, holding only its own rows.
        var share = (await container.GetBlobClient(CheckingExerciseBlobPaths.DatasetBlobName(
            _exerciseId, release, shareId, "8604070")).DownloadContentAsync()).Value.Content.ToString();
        Assert.Contains("Not a result", share);
        Assert.DoesNotContain("60181576", share);
        var included = (await container.GetBlobClient(CheckingExerciseBlobPaths.DatasetBlobName(
            _exerciseId, release, _includedId, "8604070")).DownloadContentAsync()).Value.Content.ToString();
        Assert.Contains("\"GRADE\": \"5\"", included);
        Assert.DoesNotContain("\"GRADE\": \"4\"", included);
    }

    [Fact]
    public async Task One_journey_dataset_is_written_once_as_the_merged_file()
    {
        // With one dataset that feeds the journey, the merged file already holds exactly that
        // dataset's records. A per-dataset file would be a copy of it.
        await UploadAsync(Csv("5"), Csv("4"));
        await using (var ctx = CreateContext())
        {
            ctx.CheckingWindowDatasets.Remove(await ctx.CheckingWindowDatasets.SingleAsync(d => d.Id == _nonIncludedId));
            await ctx.SaveChangesAsync();
        }

        Assert.False((await RunAsync()).IsError);

        Assert.Equal(["5"], await GradesSchoolsSeeAsync());
        var release = (await LoadExerciseAsync()).CurrentReleaseId!.Value;
        var container = _blobs.GetBlobContainerClient(_windowId.ToString());
        Assert.True((await container.GetBlobClient(CheckingExerciseBlobPaths.DataBlobName(
            _exerciseId, CheckingDataType.Results, "8604070", release)).ExistsAsync()).Value);
        Assert.False((await container.GetBlobClient(CheckingExerciseBlobPaths.DatasetBlobName(
            _exerciseId, release, _includedId, "8604070")).ExistsAsync()).Value);
    }
}
