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
public sealed class DevDataSeedingOrchestrator(
    DevDataSeeder devDataSeeder,
    IPortalDbContext dbContext,
    BlobServiceClient blobServiceClient,
    IPupilDataBlobClient pupilDataBlobClient,
    IRequestRepository requestRepository,
    IRequestStateBlobClient requestStateBlobClient,
    ICheckYourPupilDataService checkYourPupilDataService,
    ICheckingExerciseService checkingExerciseService,
    ICheckingExerciseIngress checkingExerciseIngress,
    RulesConfigSeeder rulesConfigSeeder,
    QualificationReferenceBlobClient qualificationReferenceBlobClient,
    IHostEnvironment environment,
    ILogger<DevDataSeedingOrchestrator> logger) : IDevDataSeedingOrchestrator
{
    public async Task RunAsync()
    {
        await devDataSeeder.SeedAsync();
        await SeedPost16Ingress.ExecuteSeedAsync(dbContext, blobServiceClient, checkingExerciseIngress, environment.ContentRootPath);

        // The E2E fixture windows' pupils and results (AB#296648), ingested from generated CSVs so
        // every seeded window has schemas and a release.
        await SeedExerciseFixtures.ExecuteSeedAsync(dbContext, blobServiceClient, checkingExerciseIngress, environment.ContentRootPath);

        try
        {
            await SeedChangeRequests.ExecuteSeedAsync(pupilDataBlobClient, requestRepository, requestStateBlobClient, checkYourPupilDataService, checkingExerciseService);
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
}
