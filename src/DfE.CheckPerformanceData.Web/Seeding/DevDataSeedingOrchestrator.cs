using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.QuestionFlow;
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
    IPupilDataBlobClient pupilDataBlobClient,
    IStudentResultsClient studentResultsClient,
    IQuestionFlowBlobClient questionFlowBlobClient,
    IRequestRepository requestRepository,
    IRequestStateBlobClient requestStateBlobClient,
    ICheckYourPupilDataService checkYourPupilDataService,
    ICheckingExerciseService checkingExerciseService,
    RulesConfigSeeder rulesConfigSeeder,
    GradeReferenceBlobClient gradeReferenceBlobClient,
    QualificationReferenceBlobClient qualificationReferenceBlobClient,
    IHostEnvironment environment,
    ILogger<DevDataSeedingOrchestrator> logger) : IDevDataSeedingOrchestrator
{
    public async Task RunAsync()
    {
        await devDataSeeder.SeedAsync();

        await SeedPupilData.ExecuteSeedAsync(pupilDataBlobClient);
        await SeedPupilData.ExecutePost16SeedAsync(pupilDataBlobClient);

        // AB#296648: the 16-19 exam results the incorrect-grade enquiry journey reads. Tolerates the
        // same Azurite API-version mismatch as the other blob seeds so a version skew degrades the
        // enquiry journey rather than aborting the whole seed.
        try
        {
            await SeedStudentResults.ExecuteSeedAsync(studentResultsClient);
        }
        catch (Azure.RequestFailedException ex) when (environment.IsDevelopment())
        {
            logger.LogWarning(ex, "Student results seeding skipped: Azurite returned {Status} {ErrorCode}.", ex.Status, ex.ErrorCode);
        }

        try
        {
            await SeedQuestionFlows.ExecuteSeedAsync(questionFlowBlobClient, environment.ContentRootPath);
        }
        catch (Azure.RequestFailedException ex) when (environment.IsDevelopment())
        {
            logger.LogWarning(ex, "Blob seeding skipped: Azurite returned {Status} {ErrorCode}. Pin azurite to a tag whose API version supports the current Azure.Storage.Blobs SDK if you need flows/pupils seeded locally.", ex.Status, ex.ErrorCode);
        }

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

        // AB#297130: the AODC grade reference, seeded into the same rules-config container. Also
        // seeded by GradeReferenceSeedingService on every startup; repeating it here means the admin
        // "Reset seed data" action restores it if the blob was deleted. Seed-if-missing, so this is
        // a no-op whenever it is already there.
        await SeedGradeReference.ExecuteSeedAsync(gradeReferenceBlobClient, environment.ContentRootPath);

        // AB#297848: the QualList qualification reference, seeded into the same rules-config
        // container for the same "reset seed data" reason as the grade reference above.
        await SeedQualificationReference.ExecuteSeedAsync(qualificationReferenceBlobClient, environment.ContentRootPath);
    }
}
