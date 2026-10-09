using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Seeding;

// Single source of truth for the development-data seeding sequence. Mirrors the block that
// previously ran inline in Program.cs: relational seed (countries + checking windows, which
// is destructive — it wipes change requests and checking windows), per-school pupil JSON
// blobs, question-flow config blobs, seeded change requests (Kingsmead), then the
// rules-config blobs. The question-flow and change-request uploads tolerate an Azurite
// API-version mismatch in Development only, exactly as before.
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public sealed class DevDataSeedingOrchestrator(
    DevDataSeeder devDataSeeder,
    IServiceScopeFactory scopeFactory,
    BlobServiceClient blobServiceClient,
    IReadOnlyDictionary<string, BlobServiceClient> blobClients,
    RulesConfigSeeder rulesConfigSeeder,
    QualificationReferenceBlobClient qualificationReferenceBlobClient,
    IHostEnvironment environment,
    ILogger<DevDataSeedingOrchestrator> logger) : IDevDataSeedingOrchestrator
{
    public async Task RunAsync()
    {
        await devDataSeeder.SeedAsync();

        // The KS4 fixture windows' pupils, ingested from generated CSVs so each has schemas and a
        // release, and the 16-19 windows, one for each step of the results enquiry year. Each
        // 16-19 seed does the step before it, then its own: Oct imports and validates the October
        // files, Nov adds late results 2, Feb adds the revised files and retires the four they
        // replace, and Mar adds included revised with retention and retires included revised. Each
        // window's sample files also go to ingress storage, so an admin can do a step again by hand.
        //
        // The windows share no rows and no blobs, so they seed in parallel: in sequence they are
        // about 40 ingress runs and most of the startup time. Each window gets its own scope,
        // because a DbContext (and the scoped ingress that shares it) is not thread-safe.
        await Task.WhenAll(
            InOwnScopeAsync((db, ingress) =>
                SeedExerciseFixtures.ExecuteSeedAsync(db, blobServiceClient, ingress, environment.ContentRootPath)),
            InOwnScopeAsync(async (db, ingress) =>
            {
                await SeedPost16OctoberSamples.ExecuteSeedAsync(db, blobServiceClient, ingress, environment.ContentRootPath);
                await SeedPost16OctoberSamples.WriteSamplesAsync(blobClients, logger);
            }),
            InOwnScopeAsync(async (db, ingress) =>
            {
                await SeedPost16NovemberSamples.ExecuteSeedAsync(db, blobServiceClient, ingress, environment.ContentRootPath);
                await SeedPost16NovemberSamples.WriteSamplesAsync(blobClients, logger);
            }),
            InOwnScopeAsync(async (db, ingress) =>
            {
                await SeedPost16FebruarySamples.ExecuteSeedAsync(db, blobServiceClient, ingress, environment.ContentRootPath);
                await SeedPost16FebruarySamples.WriteSamplesAsync(blobClients, logger);
            }),
            InOwnScopeAsync(async (db, ingress) =>
            {
                await SeedPost16MarchSamples.ExecuteSeedAsync(db, blobServiceClient, ingress, environment.ContentRootPath);
                await SeedPost16MarchSamples.WriteSamplesAsync(blobClients, logger);
            }));

        try
        {
            // A new scope: the window seeds wrote through their own contexts, so this scope's
            // context would still hold the windows as SeedCheckingWindows left them.
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await SeedChangeRequests.ExecuteSeedAsync(services.GetRequiredService<IPupilDataBlobClient>(),
                services.GetRequiredService<IRequestRepository>(), services.GetRequiredService<IRequestStateBlobClient>(),
                services.GetRequiredService<IRequestBlobClient>(), services.GetRequiredService<ICheckYourPupilDataService>(),
                services.GetRequiredService<ICheckingExerciseService>());
        }
        catch (Azure.RequestFailedException ex) when (environment.IsDevelopment())
        {
            logger.LogWarning(ex, "Change request seeding skipped: Azurite returned {Status} {ErrorCode}.", ex.Status, ex.ErrorCode);
        }

        // Seed the rules-config blobs (rules.json + country-languages.json) from the image-bundled
        // seed JSON. In deployed environments the rules-engine worker does this on startup; the
        // local/E2E web stack doesn't run that worker, so the web app self-seeds to keep the admin
        // rules editor usable. Idempotent and version-gated — never clobbers a newer valid blob.
        await rulesConfigSeeder.SeedAsync();

        // AB#297848: the QualList qualification reference, seeded into the same rules-config
        // container so the admin "Reset seed data" action restores it if the blob was deleted
        // (seed-if-missing, so a no-op whenever it is already there).
        await SeedQualificationReference.ExecuteSeedAsync(qualificationReferenceBlobClient, environment.ContentRootPath);
    }

    private async Task InOwnScopeAsync(Func<IPortalDbContext, ICheckingExerciseIngress, Task> seed)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await seed(scope.ServiceProvider.GetRequiredService<IPortalDbContext>(),
            scope.ServiceProvider.GetRequiredService<ICheckingExerciseIngress>());
    }
}
