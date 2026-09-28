using System.Globalization;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "16 to 19 Mar" window, where the March step is done. The seed does the February
/// seed (<see cref="SeedPost16FebruarySamples"/>), then links the included revised with retention
/// file, retires the included revised file it replaces and validates the results enquiry again. It
/// has an October, a November, a February and a March release. It also fills pupil data's aims slot,
/// a data share that feeds no journey, and replaces the summary share's file with the revised summary
/// with value added including retention, and pupil data's value added with the revised value added
/// including retention.
/// </summary>
/// <remarks>
/// The seed also writes the samples to the ingress storage account, container <see cref="Container"/>.
/// Their schemas are in <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>:
/// <list type="bullet">
/// <item><c>results/16to19_INC_REV_RET.csv</c>, in the supplier's 16-18 results data file shape →
/// <c>results-included-revised-retention_schema.json</c>. Its rows are
/// <see cref="SeedStudentResults.IncludedRevisedWithRetention"/>. Testing guide:
/// <c>docs/testing-revised-results.md</c>.</item>
/// <item><c>students/aims.csv</c>, in the supplier's pupil aims data file shape →
/// <c>students-aims_schema.json</c>. Its rows are one or two learning aims for every included
/// student.</item>
/// <item><c>summary/summary-value-added-revised-retention.csv</c> → <c>summary-retention_schema.json</c>,
/// which replaces the summary share's February file (<see cref="SeedPost16Summary"/>).</item>
/// <item><c>students/value-added-revised-retention.csv</c> → <c>students-value-added-revised-retention_schema.json</c>,
/// which replaces pupil data's February value added file (<see cref="SeedPost16ValueAdded"/>).</item>
/// </list>
/// </remarks>
public static class SeedPost16MarchSamples
{
    public const string Container = "16-to-19-mar";

    public const string IncludedRevisedWithRetentionFile = $"results/{ResultsFileTags.Post16IncludedRevisedWithRetention}.csv";
    public const string IncludedRevisedWithRetentionSchema = "results-included-revised-retention_schema.json";

    public const string AimsFile = "students/aims.csv";
    public const string AimsSchema = "students-aims_schema.json";

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
    {
        [IncludedRevisedWithRetentionFile] = SeedPost16OctoberSamples.ResultsCsv(
            SeedStudentResults.IncludedRevisedWithRetention, SeedPost16OctoberSamples.KingsmeadStudents()),
        [AimsFile] = AimsCsv(SeedPupilData.Post16Pupils(DevDataSeeder.Post16MarchCheckingWindowId).Where(p => p.Included)),
        [SeedPost16Summary.March.File] =
            SeedPost16Summary.Csv(SeedPost16Summary.March, DevDataSeeder.Post16MarchCheckingWindowId),
        [SeedPost16ValueAdded.March.File] =
            SeedPost16ValueAdded.Csv(SeedPost16ValueAdded.March, DevDataSeeder.Post16MarchCheckingWindowId)
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
        await SeedPost16Summary.AddAsync(dbContext, blobs, ingress, contentRootPath, windowId, SeedPost16Summary.March);
        await LinkAimsAsync(dbContext, blobs, contentRootPath, windowId);
        // Validates pupil data, so the aims file goes into the same release as the value added file.
        await SeedPost16ValueAdded.AddAsync(dbContext, blobs, ingress, contentRootPath, windowId, SeedPost16ValueAdded.March);
    }

    // Links the aims file to pupil data's aims slot and makes the slot required. It does not
    // validate: the value added step that follows does. A window without the slot (a test window)
    // has nothing to fill.
    private static async Task LinkAimsAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, string contentRootPath, Guid windowId)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);
        var pupilData = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.PupilData);
        if (pupilData.Datasets.SingleOrDefault(d => d.Name == SeedCheckingWindows.AimsDataset) is not { } dataset)
            return;

        await SeedExerciseFixtures.LinkAsync(blobs, pupilData, dataset, Path.GetFileName(AimsFile),
            Files()[AimsFile], AimsSchema,
            await File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "post16", AimsSchema)));
        dataset.Required = true;
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Writes the March sample files, replacing any from an earlier seed. Does nothing when no
    /// ingress storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Mar", logger);

    /// <summary>The DfE number every tenth aim is recorded at, a provider other than the student's own.</summary>
    public const string PartnerAimLaestab = "8604099";

    // A pupil aims data file in the supplier's own shape: every column of the data specification,
    // headed by its field reference, one row per student per learning aim. Every third student has
    // two aims.
    internal static byte[] AimsCsv(IEnumerable<Post16PupilRecord> students) =>
        SeedExerciseFixtures.WriteCsv(students.SelectMany((student, index) =>
            Enumerable.Range(0, index % 3 == 0 ? 2 : 1).Select(n =>
            {
                var aim = Aims[(index + n) % Aims.Length];
                var aimLaestab = (index + n) % 10 == 9 ? PartnerAimLaestab : student.Laestab;
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["LAESTAB"] = student.Laestab,
                    ["URN"] = student.Urn,
                    ["UKPRN"] = student.Ukprn,
                    ["AimLAESTAB"] = aimLaestab,
                    ["ULN"] = student.Uln,
                    ["CYPMD_ID"] = student.Cypmd_Id,
                    ["SURNAME"] = student.Surname,
                    ["FORENAMES"] = student.Firstname,
                    ["SEX"] = student.Sex,
                    ["DOB"] = student.DateOfBirth,
                    ["AGE"] = student.Age.ToString(CultureInfo.InvariantCulture),
                    ["LearningAimReference"] = aim.Reference,
                    ["Subj_Desc"] = aim.Subject,
                    ["Aim_Type"] = aim.Type.ToString(CultureInfo.InvariantCulture),
                    ["cypmd_pk"] = $"{aimLaestab}{student.Cypmd_Id}{aim.Reference}"
                };
            })));

    // Vocational learning aims: 2 = tech level, 4 = tech certification, 5 = other level 2 vocational.
    private static readonly (string Reference, string Subject, int Type)[] Aims =
    [
        ("60183123", "Applied Science", 2),
        ("60175680", "Health and Social Care", 2),
        ("60304588", "Digital Production", 4),
        ("60146412", "Construction and the Built Environment", 4),
        ("50079657", "Hospitality", 5),
        ("60171731", "Sport and Active Leisure", 5)
    ];
}
