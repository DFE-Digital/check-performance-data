using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Feb" window, where the February step is done. The seed does the November
/// seed's October import and late results 2 (<see cref="SeedPost16NovemberSamples"/>), then links the
/// two revised files, retires the four files they replace (included, non-included and both late
/// results) and validates the results enquiry again. It has an October, a November and a February
/// release.
/// </summary>
/// <remarks>
/// The seed also writes the samples to the ingress storage account, container
/// <see cref="Container"/>: <c>results/16to19_INC_REV.csv</c> →
/// <c>results-included-revised_schema.json</c> and <c>results/16to19_NONINC_REV.csv</c> →
/// <c>results-non-included-revised_schema.json</c>, both in the supplier's 16-18 results data file
/// shape, in <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>. Their rows are
/// <see cref="SeedStudentResults.Revised"/>. Testing guide: <c>docs/testing-revised-results.md</c>.
/// </remarks>
public static class SeedPost16FebruarySamples
{
    public const string Container = "16-to-19-feb";

    public const string IncludedRevisedFile = $"results/{ResultsFileTags.Post16IncludedRevised}.csv";
    public const string NonIncludedRevisedFile = $"results/{ResultsFileTags.Post16NonIncludedRevised}.csv";

    /// <summary>The slots the revised files replace. The seed retires them.</summary>
    public static readonly IReadOnlyList<string> Replaced =
    [
        ResultsFileTags.Post16Included, ResultsFileTags.Post16NonIncluded,
        ResultsFileTags.Post16LateResults1, ResultsFileTags.Post16LateResults2
    ];

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files()
    {
        var students = SeedPost16OctoberSamples.KingsmeadStudents();
        return new Dictionary<string, byte[]>
        {
            [IncludedRevisedFile] = SeedPost16OctoberSamples.ResultsCsv(
                SeedStudentResults.Revised.Where(r => r.SourceFile == ResultsFileTags.Post16IncludedRevised), students),
            [NonIncludedRevisedFile] = SeedPost16OctoberSamples.ResultsCsv(
                SeedStudentResults.Revised.Where(r => r.SourceFile == ResultsFileTags.Post16NonIncludedRevised), students)
        };
    }

    public static Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath) =>
        ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, DevDataSeeder.Post16FebruaryCheckingWindowId);

    /// <summary>Does the November seed, then adds the revised files, retires the four files they
    /// replace and validates, in the given window, which must already exist with the exercises
    /// <see cref="SeedCheckingWindows"/> gives a 16-19 window. The id is a parameter for the tests and
    /// the March window.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        await SeedPost16NovemberSamples.ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, windowId);

        var files = Files();
        await SeedPost16OctoberSamples.AddResultsFilesAsync(dbContext, blobs, ingress, contentRootPath, windowId,
            [
                (ResultsFileTags.Post16IncludedRevised, IncludedRevisedFile, files[IncludedRevisedFile],
                    "results-included-revised_schema.json"),
                (ResultsFileTags.Post16NonIncludedRevised, NonIncludedRevisedFile, files[NonIncludedRevisedFile],
                    "results-non-included-revised_schema.json")
            ],
            [.. Replaced]);
    }

    /// <summary>
    /// Writes the revised sample files, replacing any from an earlier seed. Does nothing when no
    /// ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Feb", logger);
}
