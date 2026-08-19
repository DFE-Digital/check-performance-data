using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Seeds the grade reference blob on web startup in every environment. AB#296648.
///
/// Unlike the rest of the dev-data seeding — which only runs in Development or behind
/// <c>SeedDevelopmentData</c> — the grade reference is real reference data the revised-grade picker
/// cannot work without, and Terraform provisions the rules-config container empty. So this runs
/// everywhere, exactly as the rules-engine worker self-seeds <c>rules.json</c>. It is
/// seed-if-missing, so an environment that has had the full AODC export loaded is untouched.
///
/// Failures are swallowed inside <see cref="GradeReferenceBlobClient.SeedIfMissingAsync"/> so a
/// storage blip degrades the grade picker rather than blocking startup.
/// </summary>
public sealed class GradeReferenceSeedingService(
    IServiceProvider services,
    IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<GradeReferenceBlobClient>();
        await SeedGradeReference.ExecuteSeedAsync(client, environment.ContentRootPath, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
