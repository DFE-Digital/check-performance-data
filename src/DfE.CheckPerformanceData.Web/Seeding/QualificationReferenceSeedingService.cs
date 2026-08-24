using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Seeds the qualification reference blob on web startup in every environment. AB#297848.
///
/// Unlike the rest of the dev-data seeding — which only runs in Development or behind
/// <c>SeedDevelopmentData</c> — the qualification reference is real reference data the
/// missing-qualification journey cannot work without, and Terraform provisions the rules-config
/// container empty. So this runs everywhere, exactly as <see cref="GradeReferenceSeedingService"/>.
/// It is seed-if-missing, so an environment that has had the full QualList export loaded is
/// untouched.
///
/// Failures are swallowed inside <see cref="QualificationReferenceBlobClient.SeedIfMissingAsync"/>
/// so a storage blip degrades the qualification search page rather than blocking startup.
/// </summary>
public sealed class QualificationReferenceSeedingService(
    IServiceProvider services,
    IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QualificationReferenceBlobClient>();
        await SeedQualificationReference.ExecuteSeedAsync(client, environment.ContentRootPath, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
