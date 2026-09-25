using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Nov" window, where the November step is done. The seed does the October
/// import and validation (<see cref="SeedPost16OctoberSamples"/>), then links the late results 2
/// sample and validates the results enquiry again, so it has an October and a November release.
/// </summary>
/// <remarks>
/// The seed also writes the sample to the ingress storage account, container <see cref="Container"/>:
/// <c>results/16to19_LR2.csv</c>, in the supplier's late results shape. Its schema is
/// <c>results-late-2_schema.json</c> in <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>.
/// Its rows are <see cref="SeedStudentResults.LateResults2"/>. Testing guide:
/// <c>docs/testing-late-results-2.md</c>.
/// </remarks>
public static class SeedPost16NovemberSamples
{
    public const string Container = "16-to-19-nov";

    public const string LateResults2File = $"results/{ResultsFileTags.Post16LateResults2}.csv";
    public const string LateResults2Schema = "results-late-2_schema.json";

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
    {
        // An Amendment is a row an October file already holds (same student, QAN and session).
        [LateResults2File] = SeedPost16OctoberSamples.LateResultsCsv(
            SeedStudentResults.LateResults2, SeedPost16OctoberSamples.KingsmeadStudents(), SeedStudentResults.All)
    };

    public static Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath) =>
        ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, DevDataSeeder.Post16NovemberCheckingWindowId);

    /// <summary>Imports and validates the October files, then late results 2, into the given
    /// window, which must already exist with the exercises <see cref="SeedCheckingWindows"/> gives a
    /// 16-19 window. The id is a parameter for the tests and the later windows.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        await SeedPost16OctoberSamples.ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, windowId);
        await SeedPost16OctoberSamples.AddResultsFilesAsync(dbContext, blobs, ingress, contentRootPath, windowId,
            [(ResultsFileTags.Post16LateResults2, LateResults2File, Files()[LateResults2File], LateResults2Schema)]);
    }

    /// <summary>
    /// Writes the late results 2 sample file, replacing one from an earlier seed. Does nothing when
    /// no ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Nov", logger);
}
