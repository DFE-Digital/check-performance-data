using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Mar" window, where the March step is done. The seed does the February
/// seed (<see cref="SeedPost16FebruarySamples"/>), then links the included revised with retention
/// file, retires the included revised file it replaces and validates the results enquiry again. It
/// has an October, a November, a February and a March release.
/// </summary>
/// <remarks>
/// The seed also writes the sample to the ingress storage account, container <see cref="Container"/>:
/// <c>results/16to19_INC_REV_RET.csv</c> → <c>results-included-revised-retention_schema.json</c>, in
/// the supplier's 16-18 results data file shape, in
/// <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>. Its rows are
/// <see cref="SeedStudentResults.IncludedRevisedWithRetention"/>. Testing guide:
/// <c>docs/testing-revised-results.md</c>.
/// </remarks>
public static class SeedPost16MarchSamples
{
    public const string Container = "16-to-19-mar";

    public const string IncludedRevisedWithRetentionFile = $"results/{ResultsFileTags.Post16IncludedRevisedWithRetention}.csv";
    public const string IncludedRevisedWithRetentionSchema = "results-included-revised-retention_schema.json";

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
    {
        [IncludedRevisedWithRetentionFile] = SeedPost16OctoberSamples.ResultsCsv(
            SeedStudentResults.IncludedRevisedWithRetention, SeedPost16OctoberSamples.KingsmeadStudents())
    };

    public static Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath) =>
        ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, DevDataSeeder.Post16MarchCheckingWindowId);

    /// <summary>Does the February seed, then adds the included revised with retention file, retires
    /// included revised and validates, in the given window, which must already exist with the
    /// exercises <see cref="SeedCheckingWindows"/> gives a 16-19 window. The id is a parameter for the
    /// tests.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        await SeedPost16FebruarySamples.ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, windowId);
        await SeedPost16OctoberSamples.AddResultsFilesAsync(dbContext, blobs, ingress, contentRootPath, windowId,
            [
                (ResultsFileTags.Post16IncludedRevisedWithRetention, IncludedRevisedWithRetentionFile,
                    Files()[IncludedRevisedWithRetentionFile], IncludedRevisedWithRetentionSchema)
            ],
            ResultsFileTags.Post16IncludedRevised);
    }

    /// <summary>
    /// Writes the included revised with retention sample file, replacing one from an earlier seed.
    /// Does nothing when no ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Mar", logger);
}
