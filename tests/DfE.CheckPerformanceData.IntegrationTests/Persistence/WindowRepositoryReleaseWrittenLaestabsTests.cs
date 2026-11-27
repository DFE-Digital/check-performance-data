using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

/// <summary>
/// #905: T014 — the window read path carries the written LAESTAB list on the release.
/// </summary>
public sealed class WindowRepositoryReleaseWrittenLaestabsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private static readonly Guid WindowId = Guid.NewGuid();
    private static readonly Guid ExerciseId = Guid.NewGuid();
    private static readonly Guid ReleaseId = Guid.NewGuid();
    private static readonly Guid DatasetId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = WindowId,
            Title = "Pupil data 2026",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = ExerciseId,
                    ExerciseType = CheckingExerciseType.PupilData,
                    TabName = "Pupils",
                    IsEnabled = true,
                    UsesExerciseStorage = true,
                    StartDate = new DateTime(2026, 1, 1),
                    EndDate = new DateTime(2026, 12, 31),
                    Datasets =
                    [
                        new CheckingWindowDataset
                        {
                            Id = DatasetId,
                            CheckingWindowId = WindowId,
                            Name = "pupils",
                            Included = true,
                            FeedsJourney = true,
                            SortOrder = 0
                        }
                    ],
                    Releases =
                    [
                        new CheckingExerciseRelease
                        {
                            Id = ReleaseId,
                            Number = 1,
                            PublishedAt = new DateTime(2026, 6, 1, 12, 0, 0),
                            PublishedBy = "admin@example.gov.uk",
                            FilesWritten = 3,
                            SampleLaestabs = ["9334290", "8604070"],
                            WrittenLaestabs = ["1234567", "8604070", "9334290"],
                            Files =
                            [
                                new CheckingExerciseReleaseFile
                                {
                                    Id = Guid.NewGuid(),
                                    CheckingExerciseReleaseId = ReleaseId,
                                    DatasetId = DatasetId,
                                    DatasetName = "pupils",
                                    FeedsJourney = true,
                                    Included = true,
                                    SourceFile = "pupils.csv",
                                    IngressFile = "ingest/pupils.csv",
                                    IngressFileChecksum = "abc",
                                    SchemaFile = "schema.json",
                                    SchemaFileChecksum = "def",
                                    SortOrder = 0
                                }
                            ]
                        }
                    ]
                }
            ]
        });

        await ctx.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    [Fact]
    public async Task The_release_view_carries_the_written_LAESTAB_list_with_the_existing_details()
    {
        // #905: T014 — read the window through the repository and check the release carries
        // the school list alongside the file count and the sample schools.
        await using var ctx = CreateContext();
        var window = await new WindowRepository(ctx).GetByIdAsync(WindowId, default);

        var exercise = Assert.Single(window!.Exercises);
        var release = Assert.Single(exercise.Releases);

        Assert.Equal(3, release.FilesWritten);
        Assert.Equal(["9334290", "8604070"], release.SampleLaestabs);
        Assert.Equal(["1234567", "8604070", "9334290"], release.WrittenLaestabs);
        Assert.Single(release.Files);
    }
}
