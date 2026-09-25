using System.Globalization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the sample files for the "16 to 19 Oct" window, written to the ingress storage
/// account, where the admin's "Choose CSV" page lists them. The seed does not link or ingest them:
/// the window is left for an admin to import and validate, as they would the supplier's files.
/// </summary>
/// <remarks>
/// <para>
/// Container <see cref="Container"/>: the two student files under <c>students/</c> and the three
/// October results files under <c>results/</c>. The schemas are not uploaded: the admin's "Choose
/// schema" page takes a file from their own machine, so they are in the repository under
/// <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/</c>:
/// </para>
/// <list type="bullet">
/// <item><c>students/included.csv</c> → <c>students-included_schema.json</c></item>
/// <item><c>students/nonincluded.csv</c> → <c>students-non-included_schema.json</c></item>
/// <item><c>results/16to19_INC.csv</c> → <c>results-included_schema.json</c></item>
/// <item><c>results/16to19_NONINC.csv</c> → <c>results-non-included_schema.json</c></item>
/// <item><c>results/16to19_LR1.csv</c>, in the supplier's late results shape → <c>results-late_schema.json</c></item>
/// </list>
/// <para>
/// The students come from <see cref="SeedPupilData"/> and the results from
/// <see cref="SeedStudentResults"/>, so every result names a student in the student files. There
/// is no late results 2 file: it arrives in November.
/// </para>
/// </remarks>
public static class SeedPost16OctoberSamples
{
    public const string Container = "16-to-19-oct";

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files()
    {
        var students = SeedPupilData.Post16Pupils(DevDataSeeder.Post16OctoberCheckingWindowId);
        var byCypmd = KingsmeadStudents();

        var files = new Dictionary<string, byte[]>
        {
            ["students/included.csv"] = SeedExerciseFixtures.RecordsCsv(students.Where(p => p.Included == true)),
            ["students/nonincluded.csv"] = SeedExerciseFixtures.RecordsCsv(students.Where(p => p.Included != true))
        };
        foreach (var tag in new[] { ResultsFileTags.Post16Included, ResultsFileTags.Post16NonIncluded })
            files[$"results/{tag}.csv"] = ResultsCsv(SeedStudentResults.All.Where(r => r.SourceFile == tag), byCypmd);
        files[$"results/{ResultsFileTags.Post16LateResults1}.csv"] = LateResultsCsv(
            SeedStudentResults.All.Where(r => r.SourceFile == ResultsFileTags.Post16LateResults1), byCypmd,
            SeedStudentResults.All.Where(r => r.SourceFile is ResultsFileTags.Post16Included or ResultsFileTags.Post16NonIncluded));
        return files;
    }

    /// <summary>Kingsmead's students by CYPMD id, as the sample files name them.</summary>
    internal static IReadOnlyDictionary<string, Post16PupilRecord> KingsmeadStudents() =>
        SeedPupilData.Post16Pupils(DevDataSeeder.Post16OctoberCheckingWindowId)
            .Where(p => p.Laestab == SeedStudentResults.Laestab.Replace("/", string.Empty))
            .ToDictionary(p => p.Cypmd_Id);

    /// <summary>
    /// Writes the sample files, replacing any from an earlier seed. Does nothing when no ingress
    /// storage account is configured.
    /// </summary>
    public static Task ExecuteSeedAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients, ILogger logger) =>
        WriteToIngressAsync(blobClients, Container, Files(), "16 to 19 Oct", logger);

    internal static async Task WriteToIngressAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients,
        string containerName, IReadOnlyDictionary<string, byte[]> files, string windowTitle, ILogger logger)
    {
        if (!blobClients.TryGetValue("ingress", out var ingress))
        {
            logger.LogWarning("No ingress storage account: the {Window} sample files were not written.", windowTitle);
            return;
        }

        var container = ingress.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync();
        foreach (var (path, content) in files)
            await container.GetBlobClient(path).UploadAsync(new BinaryData(content), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv" }
            });
    }

    // A 16-18 results data file in the supplier's own shape: every column of the data
    // specification, headed by its field reference. There is no QAN, QUAL_NAME, SYLLABUS or SESSION
    // column: results-included_schema.json and results-non-included_schema.json read them from GNUMBER, Short_Qual_Desc +
    // SubjectDescription, BRDSUBNO and SEASON + EXAMYEAR, as it must for the supplier's real file.
    internal static byte[] ResultsCsv(
        IEnumerable<StudentResultRecord> results, IReadOnlyDictionary<string, Post16PupilRecord> students) =>
        SeedExerciseFixtures.WriteCsv(results.Select((result, index) =>
        {
            var student = students[result.CypmdId];
            var qualification = SupplierQualifications[result.Qan];
            var year = result.Session[1..];
            return new Dictionary<string, string>
            {
                ["ULN"] = student.Uln,
                ["CYPMD_ID"] = result.CypmdId,
                ["SURNAME"] = student.Surname,
                ["FORENAMES"] = student.Firstname,
                ["SEX"] = student.Sex,
                ["DOB"] = student.DateOfBirth,
                ["AGE"] = student.Age.ToString(CultureInfo.InvariantCulture),
                ["EXAMNO"] = ((index % 9) + 1).ToString(CultureInfo.InvariantCulture),
                ["GNUMBER"] = result.Qan,
                ["AB_Code"] = qualification.AwardingBody,
                ["EXAMYEAR"] = year,
                ["SEASON"] = result.Session[..1],
                ["exam_date"] = $"15/06/{year}",
                ["Short_Qual_Desc"] = qualification.Short,
                ["SubjectDescription"] = qualification.Subject,
                ["GRADE"] = result.Grade,
                ["POINTS_1618"] = "0.00",
                ["CAPPED_PTS"] = "0.00",
                ["ANCN"] = "12345",
                ["ADFECN"] = student.Laestab,
                ["BRDSUBNO"] = result.SyllabusCode,
                ["MAPPING"] = string.Empty,
                ["Level3QualificationCategory"] = string.Empty,
                ["EMQualificationCategory"] = string.Empty,
                ["R_INCL"] = "51",
                ["R_INCL_EM"] = "58",
                ["QUAL_KS4"] = "60",
                ["UKPRN"] = student.Ukprn,
                ["URN"] = student.Urn,
                ["LAESTAB"] = student.Laestab,
                ["cypmd_pk"] = $"{student.Laestab}{result.CypmdId}{result.Qan}{year}"
            };
        }));

    // A late results file in the supplier's own shape (data specification, 1618 names): no ULN,
    // DOB or URN, and the qualification by GNUMBER, SYLLABUS_TITLE, BRDSUBNO and EXAM_YEAR_SEASON,
    // which results-late_schema.json reads into QAN, QUAL_NAME, SYLLABUS and SESSION. A row is an
    // Amendment when an earlier file holds the same student, QAN and session; otherwise it is New.
    internal static byte[] LateResultsCsv(
        IEnumerable<StudentResultRecord> results, IReadOnlyDictionary<string, Post16PupilRecord> students,
        IEnumerable<StudentResultRecord> earlierFiles)
    {
        var earlier = earlierFiles
            .Select(r => (r.CypmdId, r.Qan, r.Session))
            .ToHashSet();
        return SeedExerciseFixtures.WriteCsv(results.Select(result =>
        {
            var student = students[result.CypmdId];
            var qualification = SupplierQualifications[result.Qan];
            return new Dictionary<string, string>
            {
                ["LAESTAB"] = student.Laestab,
                ["CYPMD_ID"] = result.CypmdId,
                ["SURNAME"] = student.Surname,
                ["FORENAMES"] = student.Firstname,
                ["AB_CODE_NDAQ"] = qualification.AwardingBody,
                ["Short_Qual_Desc"] = qualification.Short,
                ["EXAM_YEAR_SEASON"] = result.Session,
                ["EXAM_DATE"] = $"{result.Session[1..]}0615",
                ["Discount_Code"] = string.Empty,
                ["SYLLABUS_TITLE"] = result.QualificationName,
                ["GNUMBER"] = result.Qan,
                ["BRDSUBNO"] = result.SyllabusCode,
                ["GRADE"] = result.Grade,
                ["Late_Result_Type"] = earlier.Contains((result.CypmdId, result.Qan, result.Session)) ? "Amendment" : "New"
            };
        }));
    }

    // The supplier's results file splits a qualification into a short type (at most 12 characters)
    // and a subject; a late file also names the awarding body. One row per QAN in
    // SeedStudentResults' catalogue.
    private static readonly IReadOnlyDictionary<string, (string Short, string Subject, string AwardingBody)> SupplierQualifications =
        new Dictionary<string, (string, string, string)>
        {
            ["60146084"] = ("GCSE", "Mathematics", "AQA"),
            ["60148366"] = ("GCSE", "English Language", "Pearson"),
            ["60149589"] = ("GCE A", "Art and Design", "Pearson"),
            ["60172186"] = ("BTEC NEC", "Sport", "Pearson"),
            ["10025480"] = ("FSMQ", "Additional Maths", "OCR"),
            ["50034157"] = ("IB", "International Baccalaureate", "IBO")
        };
}
