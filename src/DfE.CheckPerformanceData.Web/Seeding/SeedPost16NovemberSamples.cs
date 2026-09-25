using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Nov" window, where the October import is already done. The seed links
/// the October sample files (<see cref="SeedPost16OctoberSamples"/>) to their slots, as an admin
/// would, and validates both exercises, so each has a release. Only the late results 2 slot is
/// empty. Its sample file goes to the ingress storage account for an admin to add.
/// </summary>
/// <remarks>
/// Container <see cref="Container"/>: <c>results/16to19_LR2.csv</c>, in the supplier's late results
/// shape. Pair it with <c>results-late-2_schema.json</c> in
/// <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>. Its rows are
/// <see cref="SeedStudentResults.LateResults2"/>. Testing guide: <c>docs/testing-late-results-2.md</c>.
/// </remarks>
public static class SeedPost16NovemberSamples
{
    public const string Container = "16-to-19-nov";

    public const string LateResults2File = $"results/{ResultsFileTags.Post16LateResults2}.csv";

    /// <summary>What the seed imports for the admin: slot, October sample file, schema.</summary>
    public static readonly IReadOnlyList<(CheckingExerciseType Exercise, string Slot, string File, string Schema)> OctoberImport =
    [
        (CheckingExerciseType.PupilData, "included", "students/included.csv", "students-included_schema.json"),
        (CheckingExerciseType.PupilData, "nonincluded", "students/nonincluded.csv", "students-non-included_schema.json"),
        (CheckingExerciseType.ResultsEnquiry, ResultsFileTags.Post16Included, "results/16to19_INC.csv", "results-included_schema.json"),
        (CheckingExerciseType.ResultsEnquiry, ResultsFileTags.Post16NonIncluded, "results/16to19_NONINC.csv", "results-non-included_schema.json"),
        (CheckingExerciseType.ResultsEnquiry, ResultsFileTags.Post16LateResults1, "results/16to19_LR1.csv", "results-late_schema.json")
    ];

    /// <summary>The sample files for the admin, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
    {
        // An Amendment is a row an October file already holds (same student, QAN and session).
        [LateResults2File] = SeedPost16OctoberSamples.LateResultsCsv(
            SeedStudentResults.LateResults2, SeedPost16OctoberSamples.KingsmeadStudents(), SeedStudentResults.All)
    };

    public static Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath) =>
        ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, DevDataSeeder.Post16NovemberCheckingWindowId);

    /// <summary>Imports and validates the October files into the given window, which must already
    /// exist with the exercises <see cref="SeedCheckingWindows"/> gives a 16-19 window. The id is a
    /// parameter for the tests.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        var schemaFolder = Path.Combine(contentRootPath, "Data", "Ingress", "post16");
        var samples = SeedPost16OctoberSamples.Files();
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);

        foreach (var (type, slot, file, schema) in OctoberImport)
        {
            var exercise = SeedExerciseFixtures.Exercise(window, type);
            var dataset = exercise.Datasets.Single(d => d.Name == slot);
            await SeedExerciseFixtures.LinkAsync(blobs, exercise, dataset, Path.GetFileName(file), samples[file],
                schema, await File.ReadAllTextAsync(Path.Combine(schemaFolder, schema)));
        }
        await dbContext.SaveChangesAsync();

        foreach (var type in new[] { CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry })
            await SeedExerciseFixtures.IngestAsync(blobs, ingress, SeedExerciseFixtures.Exercise(window, type));
    }

    /// <summary>
    /// Writes the late results 2 sample file, replacing one from an earlier seed. Does nothing when
    /// no ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Nov", logger);
}
