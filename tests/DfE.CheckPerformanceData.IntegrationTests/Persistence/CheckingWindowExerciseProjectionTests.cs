using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #315: ICheckingExerciseService answers "is this exercise open" from the exercise rows it is
// given, so every read path that reaches Web has to carry them. These two projections are the ones
// that did not already: the landing page's open-window list and the check-your-pupil-data window
// read. Without the extra .Select the service is handed an empty list and fails closed on a window
// that is in fact open.
public sealed class CheckingWindowExerciseProjectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0);
    private static readonly Guid WindowId = Guid.NewGuid();
    private const string Laestab = "860/4070";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = WindowId,
            Title = "Post-16 2026",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            // The outer window is the union of its exercises' dates.
            StartDate = new DateTime(2026, 8, 1),
            EndDate = new DateTime(2026, 10, 31),
            NextOpportunity = new DateTime(2027, 10, 1),
            CheckingExercises =
            [
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 31),
                    SortOrder = 0
                },
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    StartDate = new DateTime(2026, 10, 1),
                    EndDate = new DateTime(2026, 10, 31),
                    SortOrder = 1
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

    private FakePupilDataBlobClient BlobClientWithPupilData()
    {
        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(WindowId, Laestab, []);
        return blobClient;
    }

    [Fact]
    public async Task The_landing_page_read_carries_the_windows_exercises_in_sort_order()
    {
        await using var ctx = CreateContext();
        var sut = new LandingPageRepository(ctx, BlobClientWithPupilData(),
            NullLogger<LandingPageRepository>.Instance);

        var windows = await sut.GetOpenWindowsAsync(Now, Laestab, CancellationToken.None);

        var window = Assert.Single(windows);
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            window.Exercises.Select(e => e.ExerciseType));
    }

    [Fact]
    public async Task The_landing_page_read_carries_each_exercises_own_dates()
    {
        await using var ctx = CreateContext();
        var sut = new LandingPageRepository(ctx, BlobClientWithPupilData(),
            NullLogger<LandingPageRepository>.Instance);

        var windows = await sut.GetOpenWindowsAsync(Now, Laestab, CancellationToken.None);

        var resultsEnquiry = Assert.Single(windows)
            .Exercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        Assert.Equal(new DateTime(2026, 10, 1), resultsEnquiry.StartDate);
        Assert.Equal(new DateTime(2026, 10, 31), resultsEnquiry.EndDate);
    }

    [Fact]
    public async Task The_check_your_pupil_data_window_read_carries_the_windows_exercises()
    {
        await using var ctx = CreateContext();
        var sut = new CheckYourPupilDataRepository(ctx, BlobClientWithPupilData(),
            new MemoryCache(new MemoryCacheOptions()));

        var window = await sut.GetCheckingWindowAsync(WindowId);

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            window.Exercises.Select(e => e.ExerciseType));
    }

    // AB#298317: the landing banner and Check your pupil data print the next opportunity, so both
    // read paths have to carry it — a projection that drops it renders the sentence silently absent.
    [Fact]
    public async Task The_landing_page_read_carries_the_next_opportunity()
    {
        await using var ctx = CreateContext();
        var sut = new LandingPageRepository(ctx, BlobClientWithPupilData(),
            NullLogger<LandingPageRepository>.Instance);

        var windows = await sut.GetOpenWindowsAsync(Now, Laestab, CancellationToken.None);

        Assert.Equal(new DateTime(2027, 10, 1), Assert.Single(windows).NextOpportunity);
    }

    [Fact]
    public async Task The_check_your_pupil_data_window_read_carries_the_next_opportunity()
    {
        await using var ctx = CreateContext();
        var sut = new CheckYourPupilDataRepository(ctx, BlobClientWithPupilData(),
            new MemoryCache(new MemoryCacheOptions()));

        var window = await sut.GetCheckingWindowAsync(WindowId);

        Assert.Equal(new DateTime(2027, 10, 1), window.NextOpportunity);
    }
}
