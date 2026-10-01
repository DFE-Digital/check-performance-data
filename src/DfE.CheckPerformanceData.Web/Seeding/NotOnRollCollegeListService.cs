using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Loads the "Not on roll" FE college list (AB#304119) on web startup in every environment, then
/// reads it again every five minutes.
///
/// Start-up, before the app takes requests: use the bundled file, copy it to the rules-config
/// container when the blob differs, then read the blob back. Like the qualification reference,
/// this is real reference data, not dev data, so it is not behind <c>SeedDevelopmentData</c>.
/// Storage failures are swallowed inside <see cref="NotOnRollCollegeListStore"/>, so a storage
/// blip never blocks startup.
/// </summary>
public sealed class NotOnRollCollegeListService(
    NotOnRollCollegeListStore store,
    IHostEnvironment environment,
    ILogger<NotOnRollCollegeListService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            environment.ContentRootPath, "Data", "NotOnRollColleges", NotOnRollCollegeList.BlobName);

        if (File.Exists(path))
        {
            var bundled = await File.ReadAllTextAsync(path, cancellationToken);
            store.UseBundled(bundled);
            await store.SeedAsync(bundled, cancellationToken);
        }
        else
        {
            logger.LogWarning("Bundled not on roll college list not found at {Path}.", path);
        }

        await store.RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await store.RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
