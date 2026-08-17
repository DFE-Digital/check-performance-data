using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: writes the per-school 16-19 exam results JSON (container <c>{windowId}</c>, blob
/// <c>results-enquiry/data/{laestab}_results.json</c>) so the incorrect-grade enquiry journey works
/// locally before the six-file ingestion pipeline exists. AB#296648.
///
/// The qualifications, QANs, syllabus codes, sessions and grades are the fixtures from the Figma
/// screens. The CYPMD ids are NOT: they are the ids <see cref="SeedPupilData"/> actually generates
/// for the seeded Post16 school, because a result must belong to a pupil the school can select —
/// results keyed to the Figma's own CYPMD id would leave the journey with a selectable pupil who
/// holds no results.
///
/// Deliberately seeds no <see cref="ResultsFileTags.Post16LateResults2"/> row, so
/// <c>ILateResultsAvailability</c> reports false locally and the "check your second late results
/// file" interstitial is on the happy path.
/// </summary>
public static class SeedStudentResults
{
    // Kingsmead School — the Post16 school the change-request seed also uses.
    private const string Laestab = "860/4070";

    // The first three included Post16 pupils SeedPupilData generates for this school
    // (Cypmd_Id = $"5{(n + 1):D5}" for n = 0, 1, 2).
    private const string StudentA = "500001";
    private const string StudentB = "500002";
    private const string StudentC = "500003";

    public static async Task ExecuteSeedAsync(IStudentResultsClient client)
        => await client.UploadResultsAsync(DevDataSeeder.Post16CheckingWindowId, Laestab, Results);

    private static readonly StudentResultRecord[] Results =
    [
        // Student A holds the same qualification twice, distinguished only by session — the case the
        // ticket calls out as the reason the result search cannot key on QAN alone.
        new()
        {
            CypmdId = StudentA, Qan = "6037116X", QualificationName = "GCSE (9-1) Bus. Studs:Single",
            SyllabusCode = "1BS0", Session = "S2024", Grade = "5", SourceFile = ResultsFileTags.Post16Main
        },
        new()
        {
            CypmdId = StudentA, Qan = "6037116X", QualificationName = "GCSE (9-1) Bus. Studs:Single",
            SyllabusCode = "1BS0", Session = "S2023", Grade = "4", SourceFile = ResultsFileTags.Post16Main
        },
        new()
        {
            CypmdId = StudentA, Qan = "60181576", QualificationName = "GCSE (9-1) French",
            SyllabusCode = "1FR0", Session = "S2024", Grade = "6", SourceFile = ResultsFileTags.Post16LateResults1
        },
        new()
        {
            CypmdId = StudentA, Qan = "60180882", QualificationName = "GCSE (9-1) Art&Des : Fine Art",
            SyllabusCode = "1AD0", Session = "S2024", Grade = "9", SourceFile = ResultsFileTags.Post16Main
        },

        // Student B: a vocational qualification, so the grade picker shows a non-GCSE scale.
        new()
        {
            CypmdId = StudentB, Qan = "60370683", QualificationName = "Pearson BTEC Level 3 National Extended Certificate in Sport",
            SyllabusCode = "31525H", Session = "S2024", Grade = "M1", SourceFile = ResultsFileTags.Post16Main
        },
        new()
        {
            CypmdId = StudentB, Qan = "10025480", QualificationName = "OCR Level 3 FSMQ Additional Mathematics",
            SyllabusCode = "6993", Session = "S2024", Grade = "B", SourceFile = ResultsFileTags.Post16LateResults1
        },
        new()
        {
            CypmdId = StudentB, Qan = "60181576", QualificationName = "GCSE (9-1) French",
            SyllabusCode = "1FR0", Session = "S2024", Grade = "3", SourceFile = ResultsFileTags.Post16Main
        },

        // Student C: a single result, so the "one obvious choice" case is covered too.
        new()
        {
            CypmdId = StudentC, Qan = "6037116X", QualificationName = "GCSE (9-1) Bus. Studs:Single",
            SyllabusCode = "1BS0", Session = "S2024", Grade = "2", SourceFile = ResultsFileTags.Post16Main
        }
    ];
}
