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
        => await client.UploadResultsAsync(DevDataSeeder.Post16CheckingWindowId, Laestab, All);

    private static IReadOnlyList<StudentResultRecord> All => [.. FigmaResults, .. GeneratedResults()];

    private static readonly StudentResultRecord[] FigmaResults =
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

    // ── The rest of the school ───────────────────────────────────────────────
    //
    // The student search on a results enquiry lists only students who hold a result, so a seed of
    // three students leaves a manual tester unable to exercise a common-surname search, the ten
    // suggestion cap, or anything else the picker does. This spreads results across both
    // populations — every third included student and every fifth non-included one — which is
    // roughly a quarter of the school.
    //
    // Deliberately NOT every student: the search restriction and the result page's empty state are
    // both only visible when some students hold nothing.
    //
    // Qualifications come from the seeded grade reference, so the revised-grade picker can always
    // list grades. Sessions and grades vary with the student so two suggestions never read alike.
    private static IEnumerable<StudentResultRecord> GeneratedResults()
    {
        // SeedPupilData: 120 included students from index 0, then 120 non-included from index 200.
        var students = Enumerable.Range(0, 120).Where(i => i % 3 == 0 && i > 2)
            .Concat(Enumerable.Range(200, 120).Where(i => i % 5 == 0));

        foreach (var (index, position) in students.Select((n, i) => (n, i)))
        {
            var cypmdId = $"5{(index + 1):D5}";

            // One qualification each, plus a second for every third student so the "which of these
            // is wrong?" choice is a real one rather than a formality.
            yield return Row(cypmdId, Catalogue[position % Catalogue.Length], position);

            if (position % 3 == 0)
                yield return Row(cypmdId, Catalogue[(position + 1) % Catalogue.Length], position + 1);
        }
    }

    private static StudentResultRecord Row(string cypmdId, Qualification qualification, int position) => new()
    {
        CypmdId = cypmdId,
        Qan = qualification.Qan,
        QualificationName = qualification.Name,
        SyllabusCode = qualification.SyllabusCode,
        Session = position % 4 == 0 ? "S2023" : "S2024",
        Grade = qualification.Grades[position % qualification.Grades.Length],
        // No LR2 row anywhere in the seed — see the class summary.
        SourceFile = position % 3 == 0 ? ResultsFileTags.Post16LateResults1 : ResultsFileTags.Post16Main
    };

    private sealed record Qualification(string Qan, string Name, string SyllabusCode, string[] Grades);

    // Every QAN here is in Web/Data/GradeReference/grade-reference.json, and the grades are drawn
    // from that file's pass grades for the qualification.
    private static readonly Qualification[] Catalogue =
    [
        new("6037116X", "GCSE (9-1) Bus. Studs:Single", "1BS0", ["4", "5", "6", "7"]),
        new("60181576", "GCSE (9-1) French", "1FR0", ["3", "5", "6", "8"]),
        new("60180882", "GCSE (9-1) Art&Des : Fine Art", "1AD0", ["5", "6", "7", "9"]),
        new("60370683", "Pearson BTEC L1/L2 Tech Award in Sport", "31525H", ["P1", "M1", "M2", "D1"]),
        new("10025480", "OCR Level 3 FSMQ: Additional Maths", "6993", ["A", "B", "C", "D"]),
        new("50034157", "IBO Level 3 International Baccalaureate Diploma", "IBDP", ["24B", "25B", "26B", "27B"])
    ];
}
