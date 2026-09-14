using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: writes the per-school 16-19 exam results JSON (container <c>{windowId}</c>, blob
/// <c>results-enquiry/data/{laestab}_results.json</c>) so the incorrect-grade enquiry journey works
/// locally before the six-file ingestion pipeline exists. AB#296648.
///
/// The QANs are real 16-19 qualifications from the QualList reference (AB#301903 — the Figma
/// screens' GCSE fixtures were KS4 QANs the 16-19 reference does not hold); sessions and grades
/// follow the Figma screens. The CYPMD ids are NOT: they are the ids <see cref="SeedPupilData"/> actually generates
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

    // AB#298317: the pupil-data-closed 16-19 window holds the same results, so the enquiry journey
    // can be walked end to end after pupil data has shut.
    public static async Task ExecuteSeedAsync(IStudentResultsClient client)
    {
        await client.UploadResultsAsync(DevDataSeeder.Post16CheckingWindowId, Laestab, All);
        await client.UploadResultsAsync(DevDataSeeder.ClosedPupilDataPost16CheckingWindowId, Laestab, All);
    }

    private static IReadOnlyList<StudentResultRecord> All => [.. FigmaResults, .. GeneratedResults()];

    private static readonly StudentResultRecord[] FigmaResults =
    [
        // Student A holds the same qualification twice, distinguished only by session — the case the
        // ticket calls out as the reason the result search cannot key on QAN alone.
        new()
        {
            CypmdId = StudentA, Qan = "60146084", QualificationName = "GCSE (9-1) Mathematics",
            SyllabusCode = "8300H", Session = "S2024", Grade = "5", SourceFile = ResultsFileTags.Post16Main
        },
        new()
        {
            CypmdId = StudentA, Qan = "60146084", QualificationName = "GCSE (9-1) Mathematics",
            SyllabusCode = "8300H", Session = "S2023", Grade = "4", SourceFile = ResultsFileTags.Post16Main
        },
        new()
        {
            CypmdId = StudentA, Qan = "60148366", QualificationName = "GCSE (9-1) English Language",
            SyllabusCode = "1EN0", Session = "S2024", Grade = "6", SourceFile = ResultsFileTags.Post16LateResults1
        },
        new()
        {
            CypmdId = StudentA, Qan = "60149589", QualificationName = "GCE A Level Art and Design",
            SyllabusCode = "9FA0", Session = "S2024", Grade = "A", SourceFile = ResultsFileTags.Post16Main
        },

        // Student B: a vocational qualification, so the grade picker shows a non-GCSE scale.
        new()
        {
            CypmdId = StudentB, Qan = "60172186", QualificationName = "BTEC L3 Nat Ext Cert in Sport",
            SyllabusCode = "31525H", Session = "S2024", Grade = "M", SourceFile = ResultsFileTags.Post16Main
        },
        new()
        {
            CypmdId = StudentB, Qan = "10025480", QualificationName = "OCR Level 3 FSMQ: Additional Maths",
            SyllabusCode = "6993", Session = "S2024", Grade = "B", SourceFile = ResultsFileTags.Post16LateResults1
        },
        new()
        {
            CypmdId = StudentB, Qan = "60148366", QualificationName = "GCSE (9-1) English Language",
            SyllabusCode = "1EN0", Session = "S2024", Grade = "3", SourceFile = ResultsFileTags.Post16Main
        },

        // Student C: a single result, so the "one obvious choice" case is covered too.
        new()
        {
            CypmdId = StudentC, Qan = "60146084", QualificationName = "GCSE (9-1) Mathematics",
            SyllabusCode = "8300H", Session = "S2024", Grade = "2", SourceFile = ResultsFileTags.Post16Main
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
    // Qualifications come from the 16-19 qualification reference, so the revised-grade picker can
    // always list grades. Sessions and grades vary with the student so two suggestions never read
    // alike.
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

    // Every QAN here is in Web/Data/QualificationReference/qualification-reference.json — the 16-19
    // reference the revised-grade picker lists (AB#301903) — and every grade is one that QAN's scale
    // holds. QUAL_NAME is deliberately the abbreviated, results-file style of name: the grade page
    // shows the reference's full title instead, which is the point of the ticket.
    private static readonly Qualification[] Catalogue =
    [
        new("60146084", "GCSE (9-1) Mathematics", "8300H", ["4", "5", "6", "7"]),
        new("60148366", "GCSE (9-1) English Language", "1EN0", ["3", "5", "6", "8"]),
        new("60149589", "GCE A Level Art and Design", "9FA0", ["A", "B", "C", "D"]),
        new("60172186", "BTEC L3 Nat Ext Cert in Sport", "31525H", ["P", "M", "D", "*"]),
        new("10025480", "OCR Level 3 FSMQ: Additional Maths", "6993", ["A", "B", "C", "D"]),
        new("50034157", "IBO Level 3 International Baccalaureate Diploma", "IBDP", ["24B", "25B", "26B", "27B"])
    ];
}
