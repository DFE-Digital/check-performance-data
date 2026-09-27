using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Feb" window, where the February step is done. The seed does the November
/// seed's October import and late results 2 (<see cref="SeedPost16NovemberSamples"/>), then links the
/// two revised files, retires the four files they replace (included, non-included and both late
/// results) and validates the results enquiry again. It has an October, a November and a February
/// release. It then fills pupil data's empty previously published revised slot, makes it required,
/// retires previously published and validates pupil data again. It also replaces the summary with value
/// added with the revised summary with value added, and pupil data's value added with the revised value
/// added.
/// </summary>
/// <remarks>
/// The seed also writes the samples to the ingress storage account, container
/// <see cref="Container"/>: <c>results/16to19_INC_REV.csv</c> →
/// <c>results-included-revised_schema.json</c> and <c>results/16to19_NONINC_REV.csv</c> →
/// <c>results-non-included-revised_schema.json</c>, both in the supplier's 16-18 results data file
/// shape, in <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>. Their rows are
/// <see cref="SeedStudentResults.Revised"/>. Testing guide: <c>docs/testing-revised-results.md</c>.
/// <c>students/previously-published-revised.csv</c> → <c>students-previously-published-revised_schema.json</c>
/// is in the supplier's previously published shape, with the same columns as October's file.
/// <c>summary/summary-value-added-revised.csv</c> → <c>summary-november-va-revised_schema.json</c> replaces
/// the summary share's November file (<see cref="SeedPost16Summary"/>).
/// <c>students/value-added-revised.csv</c> → <c>students-value-added-revised_schema.json</c> replaces
/// pupil data's November value added file (<see cref="SeedPost16ValueAdded"/>).
/// </remarks>
public static class SeedPost16FebruarySamples
{
    public const string Container = "16-to-19-feb";

    public const string IncludedRevisedFile = $"results/{ResultsFileTags.Post16IncludedRevised}.csv";
    public const string NonIncludedRevisedFile = $"results/{ResultsFileTags.Post16NonIncludedRevised}.csv";

    public const string PreviouslyPublishedRevisedFile = "students/previously-published-revised.csv";
    public const string PreviouslyPublishedRevisedSchema = "students-previously-published-revised_schema.json";

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
                SeedStudentResults.Revised.Where(r => r.SourceFile == ResultsFileTags.Post16NonIncludedRevised), students),
            [PreviouslyPublishedRevisedFile] = PreviouslyPublishedRevisedCsv(
                SeedPupilData.Post16Pupils(DevDataSeeder.Post16FebruaryCheckingWindowId).Where(p => p.Included)),
            [SeedPost16Summary.February.File] =
                SeedPost16Summary.Csv(SeedPost16Summary.February, DevDataSeeder.Post16FebruaryCheckingWindowId),
            [SeedPost16ValueAdded.February.File] =
                SeedPost16ValueAdded.Csv(SeedPost16ValueAdded.February, DevDataSeeder.Post16FebruaryCheckingWindowId)
        };
    }

    // The previously published file again, a little changed: every second included student as in
    // October, less every tenth of those, plus every sixth student from the others. The candidate
    // numbers start at 2000, not 1000.
    internal static byte[] PreviouslyPublishedRevisedCsv(IEnumerable<Post16PupilRecord> students) =>
        SeedPost16OctoberSamples.PreviouslyPublishedCsv(
            students.Where((_, index) => index % 2 == 0 ? index % 20 != 18 : index % 12 == 1),
            firstCandidateNumber: 2000);

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

        // What an admin does when the revised file lands: links it to its waiting slot, makes the
        // slot required, retires the file it replaces and validates. A window without the slot (a
        // test window) has nothing to fill.
        await SeedPost16Summary.AddAsync(dbContext, blobs, ingress, contentRootPath, windowId, SeedPost16Summary.February);
        await SeedPost16ValueAdded.AddAsync(dbContext, blobs, ingress, contentRootPath, windowId, SeedPost16ValueAdded.February);

        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);
        var pupilData = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.PupilData);
        if (pupilData.Datasets.SingleOrDefault(d => d.Name == SeedCheckingWindows.PreviouslyPublishedRevisedDataset) is not { } revised)
            return;
        await SeedExerciseFixtures.LinkAsync(blobs, pupilData, revised, Path.GetFileName(PreviouslyPublishedRevisedFile),
            files[PreviouslyPublishedRevisedFile], PreviouslyPublishedRevisedSchema,
            await File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "post16", PreviouslyPublishedRevisedSchema)));
        revised.Required = true;
        pupilData.Datasets.Single(d => d.Name == SeedCheckingWindows.PreviouslyPublishedDataset).Retired = true;
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, pupilData);
    }

    /// <summary>
    /// Writes the revised sample files, replacing any from an earlier seed. Does nothing when no
    /// ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Feb", logger);
}
