using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Feb" window, where the October import and the November late results 2
/// are already done. The seed does the November seed's October import and validation
/// (<see cref="SeedPost16NovemberSamples"/>), then links the late results 2 sample and validates the
/// results enquiry again, so it has an October and a November release. The revised files go to the
/// ingress storage account for an admin to add.
/// </summary>
/// <remarks>
/// Container <see cref="Container"/>: <c>results/16to19_INC_REV.csv</c> →
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

    /// <summary>The sample files for the admin, by their path in <see cref="Container"/>.</summary>
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

    /// <summary>Imports and validates the October files, then late results 2, into the given
    /// window, which must already exist with the exercises <see cref="SeedCheckingWindows"/> gives a
    /// 16-19 window. The id is a parameter for the tests.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        await SeedPost16NovemberSamples.ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, windowId);

        const string schema = "results-late-2_schema.json";
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);
        var results = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.ResultsEnquiry);
        var dataset = results.Datasets.Single(d => d.Name == ResultsFileTags.Post16LateResults2);
        await SeedExerciseFixtures.LinkAsync(blobs, results, dataset,
            Path.GetFileName(SeedPost16NovemberSamples.LateResults2File),
            SeedPost16NovemberSamples.Files()[SeedPost16NovemberSamples.LateResults2File],
            schema, await File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "post16", schema)));
        await dbContext.SaveChangesAsync();

        await SeedExerciseFixtures.IngestAsync(blobs, ingress, results);
    }

    /// <summary>
    /// Writes the revised sample files, replacing any from an earlier seed. Does nothing when no
    /// ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Feb", logger);
}
