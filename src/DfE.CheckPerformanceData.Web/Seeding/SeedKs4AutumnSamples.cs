using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the "Key Stage 4 Autumn" window, as an admin leaves it after the autumn import. The
/// seed links the sample files to their slots and validates the three exercises, so each has a
/// release: pupil data, the results enquiry (main results and late results 1) and the summary
/// share. Late results 2 and revised results are still awaited.
/// </summary>
/// <remarks>
/// <para>
/// The seed also writes the samples to the ingress storage account, container
/// <see cref="Container"/>, for an admin to import by hand. Their schemas are in
/// <c>src/DfE.CheckPerformanceData.Web/Data/Ingress/ks4autumn/</c>:
/// </para>
/// <list type="bullet">
/// <item><c>pupils/pupils.csv</c> → <c>pupils_schema.json</c></item>
/// <item><c>results/KS4_MAIN.csv</c> → <c>results_schema.json</c></item>
/// <item><c>results/KS4_LR1.csv</c>, in the supplier's late results shape → <c>results-late_schema.json</c></item>
/// <item><c>summary/summary.csv</c> → <c>summary_schema.json</c></item>
/// </list>
/// <para>
/// The pupils are <see cref="SeedPupilData.Ks4Pupils"/>, so the pupils the merge E2E tests drive by
/// name are unchanged. Each pupil's attainment comes from <see cref="Attainment"/>, so the pupil
/// file, the results and the summary agree. Only Kingsmead's pupils have results. All values are
/// made up.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedKs4AutumnSamples
{
    public const string Container = "ks4-autumn";

    public const string PupilsFile = "pupils/pupils.csv";
    public const string MainResultsFile = $"results/{ResultsFileTags.Ks4Main}.csv";
    public const string LateResults1File = $"results/{ResultsFileTags.Ks4LateResults1}.csv";
    public const string SummaryFile = "summary/summary.csv";

    public const string PupilsSchema = "pupils_schema.json";
    public const string ResultsSchema = "results_schema.json";
    public const string LateResultsSchema = "results-late_schema.json";
    public const string SummarySchema = "summary_schema.json";

    private static Guid WindowId => DevDataSeeder.KeyStage4AutumnCheckingWindowId;

    // Kingsmead School, the school the dev impersonation signs in as, as the supplier writes it.
    private const string KingsmeadLaestab = "8604070";

    // Abingdon School is independent, so its summary has only the independent schools report's
    // fields. The other fields are blank.
    private const string IndependentLaestab = "9316095";

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files(string contentRootPath)
    {
        var pupils = SeedPupilData.Ks4Pupils(WindowId);
        var kingsmead = pupils.Where(p => p.Laestab == KingsmeadLaestab).ToDictionary(p => p.Cypmd_Id);
        var main = MainResults(kingsmead.Values).ToList();
        var included = IncludedCodes(ReadSchema(contentRootPath, PupilsSchema));

        return new Dictionary<string, byte[]>
        {
            [PupilsFile] = PupilsCsv(pupils),
            [MainResultsFile] = MainResultsCsv(main, kingsmead),
            [LateResults1File] = LateResultsCsv(LateResults1(kingsmead), kingsmead, main),
            [SummaryFile] = SummaryCsv(ReadSchema(contentRootPath, SummarySchema), pupils, included)
        };
    }

    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath)
    {
        var samples = Files(contentRootPath);
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, WindowId);
        var pupilData = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.PupilData);
        var results = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.ResultsEnquiry);
        var summary = window.CheckingExercises.Single(e =>
            e.ExerciseType is null && e.Name == SeedCheckingWindows.OneFileSummaryExercise);

        (CheckingExercise Exercise, string Slot, string File, string Schema)[] links =
        [
            (pupilData, WindowDatasets.Pupils, PupilsFile, PupilsSchema),
            (results, ResultsFileTags.Ks4Main, MainResultsFile, ResultsSchema),
            (results, ResultsFileTags.Ks4LateResults1, LateResults1File, LateResultsSchema),
            (summary, SeedCheckingWindows.OneFileSummaryDataset, SummaryFile, SummarySchema)
        ];
        foreach (var (exercise, slot, file, schema) in links)
            await SeedExerciseFixtures.LinkAsync(blobs, exercise, exercise.Datasets.Single(d => d.Name == slot),
                Path.GetFileName(file), samples[file], schema, ReadSchema(contentRootPath, schema));
        await dbContext.SaveChangesAsync();

        foreach (var exercise in new[] { pupilData, results, summary })
            await SeedExerciseFixtures.IngestAsync(blobs, ingress, exercise);
    }

    /// <summary>
    /// Writes the sample files, replacing any from an earlier seed. Does nothing when no ingress
    /// storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients,
        string contentRootPath, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(contentRootPath),
            "Key Stage 4 Autumn", logger);

    private static string ReadSchema(string contentRootPath, string schema) =>
        File.ReadAllText(Path.Combine(contentRootPath, "Data", "Ingress", "ks4autumn", schema));

    // The codes the schema's inclusion rule includes, so the summary counts the pupils ingress
    // stamps as included.
    private static HashSet<int> IncludedCodes(string schema)
    {
        using var doc = JsonDocument.Parse(schema);
        return doc.RootElement.GetProperty("x-ingress").GetProperty("inclusion").GetProperty("included")
            .EnumerateArray().Select(code => int.Parse(code.GetString()!, CultureInfo.InvariantCulture)).ToHashSet();
    }

    // ── Attainment ───────────────────────────────────────────────────────────

    /// <summary>A pupil's made-up attainment, from the number in their CYPMD id. English and maths
    /// are GCSE grades 9-1; the other six Attainment 8 slots are point scores.</summary>
    internal sealed record Attainment(int English, int Maths, int[] Ebacc, int[] Open, bool Disadvantaged,
        bool TripleScience, bool MultipleLanguages)
    {
        public static Attainment For(PupilRecord pupil)
        {
            var n = int.Parse(pupil.Cypmd_Id, CultureInfo.InvariantCulture);
            return new Attainment(
                English: 3 + n % 6,
                Maths: 2 + n * 5 % 8,
                Ebacc: [4 + n % 4, 4 + (n + 1) % 4, 4 + (n + 2) % 4],
                Open: [3 + n % 5, 3 + (n + 1) % 5, 3 + (n + 2) % 5],
                Disadvantaged: n % 4 == 0 || n % 15 == 0,
                TripleScience: n % 3 == 0,
                MultipleLanguages: n % 7 == 0);
        }

        // English and maths count double.
        public decimal Att8 => 2 * English + 2 * Maths + Ebacc.Sum() + Open.Sum();
        public bool BasicsAtLeast(int grade) => English >= grade && Maths >= grade;
    }

    private static string Points(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    // EAL group from the first language: 1 English, 2 other than English, 3 unclassified.
    private static string EalGroup(PupilRecord pupil) => pupil.FirstLanguage switch
    {
        "ENG" or "ENB" => "1",
        "OTH" or "OTB" => "2",
        _ => "3"
    };

    // ── Pupils ───────────────────────────────────────────────────────────────

    // The supplier's pupil file: every column of pupils_schema.json, headed by its field reference.
    // The LDS ingress file has every field, MATCHREF too, though a school's download leaves
    // MATCHREF out. The prior attainment and progress 8 fields are null in
    // 2025 and 2026, so they are blank.
    private static byte[] PupilsCsv(IReadOnlyList<PupilRecord> pupils) =>
        SeedExerciseFixtures.WriteCsv(pupils.Select((pupil, index) =>
        {
            var a = Attainment.For(pupil);
            var n = int.Parse(pupil.Cypmd_Id, CultureInfo.InvariantCulture);
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["CANDNO"] = (1000 + index).ToString("D7", CultureInfo.InvariantCulture),
                ["CYPMD_ID"] = pupil.Cypmd_Id,
                ["MATCHREF"] = pupil.MatchRef.ToString(CultureInfo.InvariantCulture),
                ["SURNAME"] = pupil.Surname,
                ["FORENAME"] = pupil.Firstname,
                ["Sex"] = pupil.Sex,
                ["DOB"] = pupil.DateOfBirth,
                ["ENTRYDAT"] = pupil.EntryDate,
                ["AGE"] = pupil.Age.ToString(CultureInfo.InvariantCulture),
                ["ACTYRGRP"] = pupil.ActualYearGroup,
                ["P_INCL"] = pupil.Pincl?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["P_INCL_DESC"] = string.Empty,
                ["INP8CALC"] = string.Empty,
                ["LAESTAB"] = pupil.Laestab,
                ["URN"] = pupil.Urn.ToString(CultureInfo.InvariantCulture),
                ["UPN"] = pupil.Upn,
                ["LANG1ST"] = pupil.FirstLanguage,
                ["SENF"] = pupil.SenF,
                ["FSM6_P"] = n % 4 == 0 ? "1" : "0",
                ["CLA_PP_1_DAY"] = n % 15 == 0 ? "1" : "0",
                ["FSM6CLA1A"] = a.Disadvantaged ? "1" : "0",
                ["EALGRP"] = EalGroup(pupil),
                ["PRIORBAND"] = string.Empty,
                ["ETHNIC"] = pupil.Ethnicity,
                ["NEWMOBILE"] = pupil.NewMobile ? "1" : "0",
                ["L2BASICS_94"] = a.BasicsAtLeast(4) ? "1" : string.Empty,
                ["L2BASICS_95"] = a.BasicsAtLeast(5) ? "1" : string.Empty,
                ["L2BASICS_97"] = a.BasicsAtLeast(7) ? "1" : string.Empty,
                ["ENTRY_E_3NG_PTQ_EE"] = "9.00",
                ["ENTRY_G_PTQ_EE"] = "8.00",
                ["tripleSci_E"] = a.TripleScience ? "1" : "0",
                ["multiLan_E"] = a.MultipleLanguages ? "1" : "0",
                ["KS2_reading_outcome"] = string.Empty,
                ["KS2_mathematics_outcome"] = string.Empty,
                ["KS2_reading_Scaled_Score"] = string.Empty,
                ["KS2_mathematics_Scaled_Score"] = string.Empty,
                ["KS2_reading_special_consideration"] = string.Empty,
                ["KS2_mathematics_special_consideration"] = string.Empty,
                ["KS2_reading_TA"] = string.Empty,
                ["KS2_mathematics_TA"] = string.Empty,
                ["KS2EMSS_GRP"] = string.Empty,
                ["SLOT1ENG"] = Points(2 * a.English),
                ["SLOT2MAT"] = Points(2 * a.Maths),
                ["SLOT3EBAC"] = Points(a.Ebacc[0]),
                ["SLOT4EBAC"] = Points(a.Ebacc[1]),
                ["SLOT5EBAC"] = Points(a.Ebacc[2]),
                ["SLOT6OPEN"] = Points(a.Open[0]),
                ["SLOT7OPEN"] = Points(a.Open[1]),
                ["SLOT8OPEN"] = Points(a.Open[2]),
                ["ATT8"] = Points(a.Att8),
                ["EBACFILL"] = "3",
                ["OPENFILL"] = "3",
                ["P8SCORE"] = string.Empty,
                ["P8SCORE_ORIG"] = string.Empty,
                ["P8ADJ"] = string.Empty,
                ["P8ENG"] = string.Empty,
                ["P8MAT"] = string.Empty,
                ["EBACSCR"] = Points(a.Ebacc.Sum()),
                ["P8EBAC"] = string.Empty,
                ["OPENSCR"] = Points(a.Open.Sum()),
                ["P8OPEN"] = string.Empty,
                ["OPENGSCR"] = Points(a.Open[0] + a.Open[1]),
                ["OPENNGSCR"] = Points(a.Open[2]),
                ["SCIPATHWAY3"] = "0",
                ["SCI_EXCLUDE_PREVSCH3"] = "0",
                ["YOUNGCARER"] = n % 25 == 0 ? "1" : "0"
            };
        }));

    // ── Results ──────────────────────────────────────────────────────────────

    private sealed record Qualification(string Qan, string Name, string SyllabusCode, string AwardingBody,
        string DiscountCode);

    // GCSEs from the qualification reference (Web/Data/QualificationReference), so the revised-grade
    // picker can list their grades.
    private static readonly Qualification Maths = new("60146084", "GCSE (9-1) Mathematics", "8300H", "AQA", "2210");
    private static readonly Qualification English = new("60148366", "GCSE (9-1) English Language", "1EN0", "Pearson", "5030");
    private static readonly Qualification OcrMaths = new("60146060", "GCSE (9-1) Mathematics", "J560H", "OCR", "2210");

    private static readonly IReadOnlyDictionary<string, Qualification> ByQan =
        new[] { Maths, English, OcrMaths }.ToDictionary(q => q.Qan);

    private static ResultRecord Result(PupilRecord pupil, Qualification qualification, string session, int grade,
        string file) => new()
    {
        CypmdId = pupil.Cypmd_Id, Qan = qualification.Qan, QualificationName = qualification.Name,
        SyllabusCode = qualification.SyllabusCode, Session = session,
        Grade = grade.ToString(CultureInfo.InvariantCulture), SourceFile = file
    };

    // English and maths for every third included Kingsmead pupil and every fifth non-included one,
    // so the results search lists some pupils and not others. Alice Smith (000001) also sat maths
    // early, in S2024, so she holds the same QAN in two sessions.
    private static IEnumerable<ResultRecord> MainResults(IEnumerable<PupilRecord> kingsmead)
    {
        foreach (var pupil in kingsmead.OrderBy(p => p.Cypmd_Id, StringComparer.Ordinal))
        {
            var n = int.Parse(pupil.Cypmd_Id, CultureInfo.InvariantCulture);
            if (!(n <= 200 ? n % 3 == 1 : n % 5 == 1)) continue;

            var a = Attainment.For(pupil);
            if (n == 1)
                yield return Result(pupil, Maths, "S2024", Math.Max(1, a.Maths - 1), ResultsFileTags.Ks4Main);
            yield return Result(pupil, Maths, "S2025", a.Maths, ResultsFileTags.Ks4Main);
            yield return Result(pupil, English, "S2025", a.English, ResultsFileTags.Ks4Main);
        }
    }

    // Late results 1: an Amendment to Alice Smith's English grade, and a New result for Bob Smith
    // (000002), who holds no main result, so he only appears in the results search after it.
    private static IReadOnlyList<ResultRecord> LateResults1(IReadOnlyDictionary<string, PupilRecord> kingsmead)
    {
        var alice = kingsmead["000001"];
        return
        [
            Result(alice, English, "S2025", Attainment.For(alice).English + 1, ResultsFileTags.Ks4LateResults1),
            Result(kingsmead["000002"], OcrMaths, "S2025", 5, ResultsFileTags.Ks4LateResults1)
        ];
    }

    // The supplier's KS4 results file: every column of results_schema.json, headed by its field
    // reference. There is no QAN, QUAL_NAME, SYLLABUS or SESSION column: the schema reads them from
    // GNUMBER, QUALIFICATION_DESCRIPTION, BRDSUBNO and SEASON_AND_YEAR.
    private static byte[] MainResultsCsv(IReadOnlyList<ResultRecord> results,
        IReadOnlyDictionary<string, PupilRecord> pupils) =>
        SeedExerciseFixtures.WriteCsv(results.Select((result, index) =>
        {
            var pupil = pupils[result.CypmdId];
            var qualification = ByQan[result.Qan];
            var early = result.Session != "S2025";
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["CANDNO"] = (1000 + int.Parse(pupil.Cypmd_Id, CultureInfo.InvariantCulture)).ToString("D7", CultureInfo.InvariantCulture),
                ["CYPMD_ID"] = result.CypmdId,
                ["SURNAME"] = pupil.Surname,
                ["FORENAME"] = pupil.Firstname,
                ["Sex"] = pupil.Sex,
                ["DOB"] = PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth),
                ["AGE"] = pupil.Age.ToString(CultureInfo.InvariantCulture),
                ["P_INCL"] = pupil.Pincl?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["LAESTAB"] = pupil.Laestab,
                ["URN"] = pupil.Urn.ToString(CultureInfo.InvariantCulture),
                ["UPN"] = pupil.Upn,
                ["EXAMNO"] = (index % 9 + 1).ToString(CultureInfo.InvariantCulture),
                ["AB_CODE_NDAQ"] = qualification.AwardingBody,
                ["QUALIFICATION_TYPE"] = "GCSE (9-1) Full Course",
                ["PTQ_INCLUDE"] = "Y",
                // An early maths entry is discounted by the later, better result.
                ["R_INCL"] = early ? "42" : "41",
                ["SEASON_AND_YEAR"] = result.Session,
                ["FIRST_EXAM_DATE"] = $"1506{result.Session[1..]}",
                ["WOLF_DISC_CODE"] = qualification.DiscountCode,
                ["WOLF_INCLUDED"] = "2",
                ["QUALIFICATION_DESCRIPTION"] = result.QualificationName,
                ["GNUMBER"] = result.Qan,
                ["BRDSUBNO"] = result.SyllabusCode,
                ["GRADE"] = result.Grade,
                ["SCORE"] = string.Empty,
                ["KS2_SPECIAL_CONSIDERATION"] = string.Empty,
                ["A8POINTS"] = Points(int.Parse(result.Grade, CultureInfo.InvariantCulture)),
                ["ANCN"] = "12345",
                ["SCHOOL_EXAM"] = "Y",
                ["DISC3_EBACC_PTQ_EE"] = early ? "Y" : string.Empty,
                ["A8DISC"] = early ? "Y" : string.Empty,
                ["ACADEMIC"] = "Y",
                ["EXAM_YR_7_8"] = string.Empty,
                ["First_Entry_Rules"] = string.Empty,
                ["Best_Entry_Rules"] = "Y",
                ["Prev_School_Rules"] = string.Empty,
                ["EngPath"] = qualification == English ? "1" : "0",
                ["SciPath"] = "0",
                ["FvB_Entry_Rules"] = string.Empty
            };
        }));

    // A KS4 late results file in the supplier's own shape (KS4 names), which
    // results-late_schema.json reads into QAN, QUAL_NAME, SYLLABUS and SESSION. A row is an
    // Amendment when the main file holds the same pupil, QAN and session; otherwise it is New.
    private static byte[] LateResultsCsv(IReadOnlyList<ResultRecord> results,
        IReadOnlyDictionary<string, PupilRecord> pupils, IEnumerable<ResultRecord> earlierFiles)
    {
        var earlier = earlierFiles.Select(r => (r.CypmdId, r.Qan, r.Session)).ToHashSet();
        return SeedExerciseFixtures.WriteCsv(results.Select(result =>
        {
            var pupil = pupils[result.CypmdId];
            var qualification = ByQan[result.Qan];
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["LAESTAB"] = pupil.Laestab,
                ["CYPMD_ID"] = result.CypmdId,
                ["SURNAME"] = pupil.Surname,
                ["FORENAME"] = pupil.Firstname,
                ["AB_CODE_NDAQ"] = qualification.AwardingBody,
                ["QUALIFICATION_TYPE"] = "GCSE (9-1) Full Course",
                ["SEASON_AND_YEAR"] = result.Session,
                ["EXAM_DATE"] = $"{result.Session[1..]}0615",
                ["WOLF_DISC_CODE"] = qualification.DiscountCode,
                ["QUALIFICATION_DESCRIPTION"] = result.QualificationName,
                ["GNUMBER"] = result.Qan,
                ["BRDSUBNO"] = result.SyllabusCode,
                ["GRADE"] = result.Grade,
                ["Late_Result_Type"] = earlier.Contains((result.CypmdId, result.Qan, result.Session)) ? "Amendment" : "New"
            };
        }));
    }

    // ── Summary ──────────────────────────────────────────────────────────────

    // The fields of the independent schools report. The other reports are the state schools one
    // (every field) and two subsets that the seeded schools do not need.
    private static readonly HashSet<string> IndependentFields =
    [
        "TPUP", "ATT8SCR", "PTL2BASICS_95", "PTL2BASICS_94", "PTmultiLan_E", "PTtripleSci_E",
        "ATT8SCRENG", "ATT8SCRMAT", "ATT8SCREBAC", "ATT8SCROPEN", "ATT8SCROPENG", "ATT8SCROPENNG"
    ];

    // One row per school with KS4 pupils, with a value for every column of the schema, in schema
    // order. The values are worked out from the school's included pupils, so the summary agrees
    // with the pupil file. Prior attainment and progress 8 are null in 2025 and 2026, so those
    // fields, and every field by prior attainment band, are blank.
    private static byte[] SummaryCsv(string schema, IReadOnlyList<PupilRecord> pupils, HashSet<int> includedCodes)
    {
        using var doc = JsonDocument.Parse(schema);
        var columns = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

        return SeedExerciseFixtures.WriteCsv(pupils
            .GroupBy(p => p.Laestab)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(school =>
            {
                var cohort = school.Where(p => p.Pincl is { } code && includedCodes.Contains(code)).ToList();
                var values = SummaryValues(school.Key, cohort);
                return (IReadOnlyDictionary<string, string>)columns.ToDictionary(c => c, c =>
                    school.Key == IndependentLaestab && c != "LAESTAB" && !IndependentFields.Contains(c)
                        ? string.Empty
                        : values.TryGetValue(c, out var value)
                            ? value
                            : NullIn2025And2026(c)
                                ? string.Empty
                                : throw new InvalidOperationException($"The KS4 Autumn summary seed has no value for column '{c}'."));
            }));
    }

    // The progress 8 and prior attainment fields, which the specification sets to null in 2025 and
    // 2026, and the fields by prior attainment band.
    private static bool NullIn2025And2026(string column) =>
        column.StartsWith("P8", StringComparison.Ordinal) || column.Contains("PRIOR", StringComparison.Ordinal)
        || column is "TP8ADJ"
        || column.EndsWith("_LO", StringComparison.Ordinal) || column.EndsWith("_MID", StringComparison.Ordinal)
        || column.EndsWith("_HI", StringComparison.Ordinal)
        || column.StartsWith("PTBASICSLO", StringComparison.Ordinal) || column.StartsWith("PTBASICSAV", StringComparison.Ordinal)
        || column.StartsWith("PTBASICSHI", StringComparison.Ordinal);

    private static Dictionary<string, string> SummaryValues(string laestab, IReadOnlyList<PupilRecord> cohort)
    {
        var attainment = cohort.Select(Attainment.For).ToList();
        var disadvantaged = attainment.Where(a => a.Disadvantaged).ToList();

        string Count(Func<PupilRecord, bool> test) => cohort.Count(test).ToString(CultureInfo.InvariantCulture);
        string Percent(int count, int of) => of == 0 ? "NA" : (100m * count / of).ToString("0.0", CultureInfo.InvariantCulture);
        string PercentOf(Func<PupilRecord, bool> test) => Percent(cohort.Count(test), cohort.Count);
        string Average(IReadOnlyList<Attainment> group, Func<Attainment, decimal> value) =>
            group.Count == 0 ? "NA" : group.Average(value).ToString("0.0", CultureInfo.InvariantCulture);
        string Basics(IReadOnlyList<Attainment> group, int grade) => Percent(group.Count(a => a.BasicsAtLeast(grade)), group.Count);

        return new Dictionary<string, string>
        {
            ["LAESTAB"] = laestab,
            ["TPUP"] = cohort.Count.ToString(CultureInfo.InvariantCulture),
            ["TNMOB"] = Count(p => !p.NewMobile),
            ["PTNMOB"] = PercentOf(p => !p.NewMobile),
            ["TFSM6CLA1A"] = disadvantaged.Count.ToString(CultureInfo.InvariantCulture),
            ["PTFSM6CLA1A"] = Percent(disadvantaged.Count, cohort.Count),
            ["TEALGRP2"] = Count(p => EalGroup(p) == "2"),
            ["PTEALGRP2"] = PercentOf(p => EalGroup(p) == "2"),
            ["SENE4"] = Count(p => p.SenF == "E"),
            ["PSENE4"] = PercentOf(p => p.SenF == "E"),
            ["SENK4"] = Count(p => p.SenF == "K"),
            ["PSENK4"] = PercentOf(p => p.SenF == "K"),
            ["ATT8SCR"] = Average(attainment, a => a.Att8),
            ["PTL2BASICS_95"] = Basics(attainment, 5),
            ["PTL2BASICS_97"] = Basics(attainment, 7),
            ["PTL2BASICS_94"] = Basics(attainment, 4),
            ["PTmultiLan_E"] = Percent(attainment.Count(a => a.MultipleLanguages), attainment.Count),
            ["PTtripleSci_E"] = Percent(attainment.Count(a => a.TripleScience), attainment.Count),
            ["ATT8SCRENG"] = Average(attainment, a => 2 * a.English),
            ["ATT8SCRMAT"] = Average(attainment, a => 2 * a.Maths),
            ["ATT8SCREBAC"] = Average(attainment, a => a.Ebacc.Sum()),
            ["ATT8SCROPEN"] = Average(attainment, a => a.Open.Sum()),
            ["ATT8SCROPENG"] = Average(attainment, a => a.Open[0] + a.Open[1]),
            ["ATT8SCROPENNG"] = Average(attainment, a => a.Open[2]),
            ["ATT8SCR_FSM6CLA1A"] = Average(disadvantaged, a => a.Att8),
            ["ATT8SCRENG_FSM6CLA1A"] = Average(disadvantaged, a => 2 * a.English),
            ["ATT8SCRMAT_FSM6CLA1A"] = Average(disadvantaged, a => 2 * a.Maths),
            ["ATT8SCREBAC_FSM6CLA1A"] = Average(disadvantaged, a => a.Ebacc.Sum()),
            ["ATT8SCROPEN_FSM6CLA1A"] = Average(disadvantaged, a => a.Open.Sum()),
            ["ATT8SCROPENG_FSM6CLA1A"] = Average(disadvantaged, a => a.Open[0] + a.Open[1]),
            ["ATT8SCROPENNG_FSM6CLA1A"] = Average(disadvantaged, a => a.Open[2]),
            ["PTFSM6CLA1ABASICS_97"] = Basics(disadvantaged, 7),
            ["PTFSM6CLA1ABASICS_95"] = Basics(disadvantaged, 5)
        };
    }
}
