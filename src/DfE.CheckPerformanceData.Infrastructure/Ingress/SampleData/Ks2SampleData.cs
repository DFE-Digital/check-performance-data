using System.Globalization;
using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using static DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData.SampleValues;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>
/// #939: the made-up rows of one school in a Key Stage 2 window. The pupil list and the summary
/// agree: the summary's cohort, boys, SEN and disadvantaged counts are those of the pupil list.
/// The KS2 dev seed data uses the same rows.
/// </summary>
/// <remarks>
/// Each school has <see cref="Cohort"/> included pupils (201). It also has
/// <see cref="NonIncludedPerSchool"/> non-included pupils: 202 (not at the end of KS2, so no KS2
/// results). Only the December file has 203 (excluded at the school's request, with results):
/// there every other non-included pupil is 203. An outcome follows its scaled score, and a progress
/// score is the attainment score less the predicted score. Values are by property name. INCLUDED,
/// INCLUDABLE and P_INCL_DESC are not written: ingress stamps them from the schema.
/// </remarks>
public static class Ks2SampleData
{
    public const int NonIncludedPerSchool = 6;

    /// <summary>The school's KS2 cohort: the included pupils of the pupil list.</summary>
    public static int Cohort(int school) => 30 + school * 7;

    /// <summary>The boys in the school's cohort. The rest are girls.</summary>
    public static int Boys(int school) => Cohort(school) / 2 + school % 3;

    public static IEnumerable<Dictionary<string, string>> Pupils(string urn, string laestab, int school, bool december)
    {
        var cohort = Cohort(school);
        var boys = Boys(school);
        for (var i = 0; i < cohort; i++)
            yield return Pupil(urn, laestab, school, i, "201", i < boys ? "M" : "F", cohort);
        for (var i = 0; i < NonIncludedPerSchool; i++)
            yield return Pupil(urn, laestab, school, cohort + i, december && i % 2 == 1 ? "203" : "202",
                i % 2 == 0 ? "F" : "M", cohort);
    }

    private static readonly string[] Ethnicities = ["WBRI", "WBRI", "WBRI", "WIRI", "WOTH", "MWBC", "AIND", "APKN", "BAFR", "BCRB", "CHNE"];
    private static readonly string[] OtherLanguages = ["OTH", "OTB"];

    private static Dictionary<string, string> Pupil(string urn, string laestab, int school, int i, string pincl,
        string sex, int cohort)
    {
        // A different name pair for every pupil of a school (7 and 400 share no factor), and a
        // different run of names in each school.
        var name = (i * 7 + school * 13) % (Firstnames.Count * Surnames.Count);
        // Year 6 in 2025/26: born from 1 September 2014 to 31 August 2015.
        var dob = new DateOnly(2014, 9, 1).AddDays(i * 37 % 365);
        // A mobile pupil joined in the last two years; everyone else joined in reception.
        var mobile = (i + 11) % cohort < 2 + school % 3;
        var entry = mobile ? new DateOnly(2024, 10, 14).AddDays(i * 13 % 200) : new DateOnly(2019, 9, 2);
        // Bands of prior attainment, rotated so they do not follow sex.
        var rotated = (i + 3) % cohort;
        var ks1Group = rotated < cohort / 5 ? 1 : rotated < cohort / 5 + cohort * 3 / 10 ? 3 : 2;
        // EHC plans, then SEN support, in the numbers the summary gives.
        var senPosition = (i + 5) % cohort;
        var ehcPlans = 1 + school % 3;
        var sen = senPosition < ehcPlans ? "E" : senPosition < ehcPlans + 4 + school ? "K" : "N";
        var eal = (i + 7) % cohort < 2 + school * 3;
        var language = eal ? OtherLanguages[i % 2] : i % 9 == 4 ? "ENB" : "ENG";
        var disadvantaged = (i + 17) % cohort < cohort / 4;

        var row = new Dictionary<string, string>
        {
            ["LAESTAB"] = laestab,
            ["URN"] = urn,
            // Letter, LAESTAB, year, serial: 13 characters, unique in the school.
            ["UPN"] = $"K{laestab}25{i:D3}",
            ["CYPMD_ID"] = $"2{school:D2}{i:D4}",
            ["MATCHREF"] = $"5{school:D2}{i:D4}",
            ["P_INCL"] = pincl,
            ["SURNAME"] = Surnames[name / Firstnames.Count],
            ["FORENAME"] = Firstnames[name % Firstnames.Count],
            ["SEX"] = sex,
            ["DOB"] = Date(dob),
            ["ENTRYDAT"] = Date(entry),
            ["SENF"] = sen,
            ["ETHNIC"] = Ethnicities[(i + school) % Ethnicities.Length],
            ["LANG1ST"] = language,
            ["EALGRP"] = eal ? "2" : "1",
            ["FSM"] = disadvantaged && i % 3 != 0 ? "1" : "0",
            ["FSM6_P"] = disadvantaged ? "1" : "0",
            ["YOUNGCARER"] = i % 23 == 9 ? "1" : "0",
            ["NEWMOBILE"] = mobile ? "1" : "0",
            // December file only.
            ["CLA_PP_1_DAY"] = i % 31 == 12 ? "1" : "0",
            ["FSM6CLA1A"] = disadvantaged || i % 31 == 12 ? "1" : "0"
        };

        // A pupil not at the end of KS2 (202) has no KS1 or KS2 results.
        foreach (var (field, value) in pincl == "202" ? NoResults() : Results(i, ks1Group))
            row[field] = value;
        return row;
    }

    private static IEnumerable<(string, string)> NoResults() =>
        ResultFields.Select(field => (field, string.Empty));

    private static readonly string[] ResultFields =
    [
        "KS1READ", "KS1WRIT", "KS1MAT", "KS1AVERAGEPS", "KS1GROUP", "READOUTCOME", "MATOUTCOME", "GPSOUTCOME",
        "READSCORE", "MATSCORE", "GPSSCORE", "READMRK", "MATMRK", "GPSMRK", "READSPECCON", "MATSPECCON",
        "GPSSPECCON", "ELIGRWM", "RWMEXP", "WRITTAOUTCOME", "SCITAOUTCOME", "MATTAOUTCOME", "READTAOUTCOME",
        "INREADPROG", "INWRITPROG", "INMATPROG", "KS1AVERAGE_GRP_P", "KS2READSCORE", "KS2WRITSCORE",
        "KS2MATSCORE", "KS2READPRED_EM", "KS2WRITPRED_EM", "KS2MATPRED_EM", "READPROGSCORE_EM_ADJUSTED",
        "WRITPROGSCORE_EM_ADJUSTED", "MATPROGSCORE_EM_ADJUSTED"
    ];

    private static IEnumerable<(string, string)> Results(int i, int ks1Group)
    {
        // 0 to 30: how this pupil does inside their prior attainment band.
        var ability = i * 17 % 31;
        // Low: 5.00 to 6.75; middle: 7.00 to 8.00; high: 8.25 to 10.00, in steps of 0.25.
        var aps = ks1Group switch
        {
            1 => 5m + ability % 8 * 0.25m,
            2 => 7m + ability % 5 * 0.25m,
            _ => 8.25m + ability % 8 * 0.25m
        };
        var ks1Outcome = ks1Group switch { 1 => "WTS", 2 => "EXS", _ => "GDS" };
        // Prior attainment group for progress: 1 to 20 over the 5.00 to 10.00 range.
        var attainmentGroup = Math.Clamp((int)((aps - 5m) / 0.25m) + 1, 1, 20);

        // One pupil in 29 was absent from the reading test: no score, no mark, and outcome A.
        var absentReading = i % 29 == 3;
        var read = absentReading ? (int?)null : Math.Min(120, 88 + ks1Group * 5 + ability % 9);
        var mat = Math.Min(120, 87 + ks1Group * 5 + (ability + 4) % 10);
        var gps = Math.Min(120, 89 + ks1Group * 5 + (ability + 2) % 9);
        var writing = ability > 24 ? "GDS" : ks1Group >= 2 || ability > 10 ? "EXS" : "WTS";
        var science = ks1Group == 1 && ability < 10 ? "HNM" : "EXS";
        var writingScore = writing switch { "GDS" => 113, "EXS" => 103, _ => 91 };
        var specialConsideration = i % 37 == 5 ? "2" : string.Empty;

        yield return ("KS1READ", ks1Outcome);
        yield return ("KS1WRIT", ks1Group == 3 && ability < 6 ? "EXS" : ks1Outcome);
        yield return ("KS1MAT", ks1Outcome);
        yield return ("KS1AVERAGEPS", TwoPlaces(aps));
        yield return ("KS1GROUP", Number(ks1Group));
        yield return ("READOUTCOME", read is null ? "A" : Outcome(read.Value));
        yield return ("MATOUTCOME", Outcome(mat));
        yield return ("GPSOUTCOME", Outcome(gps));
        yield return ("READSCORE", read is null ? "N" : Number(read.Value));
        yield return ("MATSCORE", Number(mat));
        yield return ("GPSSCORE", Number(gps));
        yield return ("READMRK", read is null ? string.Empty : Number(Mark(read.Value, 50)));
        yield return ("MATMRK", Number(Mark(mat, 110)));
        yield return ("GPSMRK", Number(Mark(gps, 70)));
        yield return ("READSPECCON", string.Empty);
        yield return ("MATSPECCON", specialConsideration);
        yield return ("GPSSPECCON", string.Empty);
        yield return ("ELIGRWM", read is null ? "0" : "1");
        yield return ("RWMEXP", read >= 100 && mat >= 100 && writing != "WTS" ? "1" : "0");
        yield return ("WRITTAOUTCOME", writing);
        yield return ("SCITAOUTCOME", science);
        // Teacher assessment of reading and maths is for pupils working below the test standard.
        yield return ("MATTAOUTCOME", string.Empty);
        yield return ("READTAOUTCOME", string.Empty);
        yield return ("INREADPROG", read is null ? "0" : "1");
        yield return ("INWRITPROG", "1");
        yield return ("INMATPROG", "1");
        yield return ("KS1AVERAGE_GRP_P", Number(attainmentGroup));

        // The predicted score is the average for the prior attainment group; progress is the
        // attainment score less the prediction.
        foreach (var (subject, score, offset) in new[] { ("READ", read, 0.0m), ("WRIT", writingScore, 0.3m), ("MAT", mat, -0.2m) })
        {
            var predicted = 95m + attainmentGroup * 0.6m + offset;
            yield return ($"KS2{subject}SCORE", score is null ? string.Empty : Number(score.Value));
            yield return ($"KS2{subject}PRED_EM", TwoPlaces(predicted));
            yield return ($"{subject}PROGSCORE_EM_ADJUSTED", score is null ? string.Empty : TwoPlaces(score.Value - predicted));
        }
    }

    // AS: reached the expected standard (a scaled score of 100); NS: did not.
    private static string Outcome(int score) => score >= 100 ? "AS" : "NS";

    // The raw mark that maps to a scaled score from 80 to 120, out of the test's total.
    private static int Mark(int score, int total) => (score - 80) * total / 40;

    // Reading, writing and maths together, then reading, writing TA, maths and GPS.
    private static readonly string[] Subjects = ["RWM", "READ", "WRITTA", "MAT", "GPS"];

    /// <summary>
    /// The school's summary row: one row for each school. <paramref name="suppressDisadvantaged"/>
    /// writes the supplier's "+" for the disadvantaged percentages, as for a small group.
    /// </summary>
    public static Dictionary<string, string> Summary(string urn, string laestab, int school, bool suppressDisadvantaged)
    {
        var cohort = Cohort(school);
        var boys = Boys(school);
        var low = cohort / 5;
        var high = cohort * 3 / 10;
        var nonMobile = cohort - 2 - school % 3;
        var eal = 2 + school * 3;
        var ehc = 1 + school % 3;
        var senSupport = 4 + school;
        var disadvantaged = suppressDisadvantaged ? 3 : cohort / 4;

        var row = new Dictionary<string, string>
        {
            ["LAESTAB"] = laestab,
            ["URN"] = urn,
            ["TELIG"] = Number(cohort),
            ["BELIG"] = Number(boys),
            ["GELIG"] = Number(cohort - boys),
            ["PBELIG"] = Percent(boys, cohort),
            ["PGELIG"] = Percent(cohort - boys, cohort),
            ["TKS1AVERAGE"] = (15.2m + school * 0.7m).ToString("0.0", CultureInfo.InvariantCulture),
            ["TKS1GROUP_L"] = Number(low),
            ["PTKS1GROUP_L"] = Percent(low, cohort),
            ["TKS1GROUP_M"] = Number(cohort - low - high),
            ["PTKS1GROUP_M"] = Percent(cohort - low - high, cohort),
            ["TKS1GROUP_H"] = Number(high),
            ["PTKS1GROUP_H"] = Percent(high, cohort),
            ["TMOBN"] = Number(nonMobile),
            ["PTMOBN"] = Percent(nonMobile, cohort),
            ["TEALGRP2"] = Number(eal),
            ["PTEALGRP2"] = Percent(eal, cohort),
            ["TSENELE"] = Number(ehc),
            ["PSENELE"] = Percent(ehc, cohort),
            ["TSENELK"] = Number(senSupport),
            ["PSENELK"] = Percent(senSupport, cohort),
            ["PTRWM_EXP"] = Pct(58 + school * 4),
            ["PTRWM_HIGH"] = Pct(6 + school * 2),
            ["READPROG_WEB"] = Progress(school, 0),
            ["READCOV"] = Pct(96 - school % 3),
            ["WRITPROG_WEB"] = Progress(school, 1),
            ["WRITCOV"] = Pct(97 - school % 2),
            ["MATPROG_WEB"] = Progress(school, 2),
            ["MATCOV"] = Pct(95 + school % 4),
            ["READ_AVERAGE"] = Number(104 + school % 4),
            ["GPS_AVERAGE"] = Number(105 + school % 3),
            ["MAT_AVERAGE"] = Number(103 + school % 5),
            ["TFSM6CLA1A"] = Number(disadvantaged),
            ["PTFSM6CLA1A"] = Percent(disadvantaged, cohort),
            ["PTRWM_EXP_FSM6CLA1A"] = suppressDisadvantaged ? "+" : Pct(44 + school * 3),
            ["PTRWM_HIGH_FSM6CLA1A"] = suppressDisadvantaged ? "+" : Pct(2 + school)
        };

        // December file only: each progress score as separate fields, which agree with the
        // *PROG_WEB value above.
        foreach (var (code, subject) in new[] { ("READ", 0), ("WRIT", 1), ("MAT", 2) })
        {
            var (score, lower, upper) = ProgressScore(school, subject);
            row[$"{code}PROG_DESCR"] = ProgressBand(score, lower, upper);
            row[$"{code}PROG"] = TwoPlaces(score);
            row[$"{code}PROG_SIGNIFICANCE"] = lower > 0 || upper < 0 ? "1" : "0";
            row[$"{code}PROG_LOWER"] = TwoPlaces(lower);
            row[$"{code}PROG_UPPER"] = TwoPlaces(upper);
        }

        // Each subject: expected standard, higher standard, and absent or unable to access.
        // Writing is teacher assessed, so its third measure is absent or disapplied.
        foreach (var code in Subjects.Skip(1))
        {
            row[$"PT{code}_EXP"] = Pct(70 + school * 3 + code.Length % 4);
            row[$"PT{code}_HIGH"] = Pct(18 + school * 2 + code.Length % 5);
            row[code == "WRITTA" ? "PTWRITTA_AD" : $"PT{code}_AT"] = Pct(school % 3);
        }

        // Each subject by prior attainment band: low, middle and high prior attainers.
        foreach (var code in Subjects)
        {
            row[$"PT{code}_EXP_L"] = Pct(22 + school * 2 + code.Length % 3);
            row[$"PT{code}_HIGH_L"] = Pct(school % 2);
            row[$"PT{code}_EXP_M"] = Pct(68 + school * 2 + code.Length % 4);
            row[$"PT{code}_HIGH_M"] = Pct(7 + school + code.Length % 3);
            row[$"PT{code}_EXP_H"] = Pct(91 + school % 5);
            row[$"PT{code}_HIGH_H"] = Pct(34 + school * 2 + code.Length % 5);
        }

        return row;
    }

    // A progress score with its confidence interval, in the supplier's own format:
    // READPROG + " (" + READPROG_LOWER + " to " + READPROG_UPPER + ")".
    private static string Progress(int school, int subject)
    {
        var (score, lower, upper) = ProgressScore(school, subject);
        return $"{TwoPlaces(score)} ({TwoPlaces(lower)} to {TwoPlaces(upper)})";
    }

    private static (decimal Score, decimal Lower, decimal Upper) ProgressScore(int school, int subject)
    {
        var score = ((school * 37 + subject * 13) % 41 - 20) / 10m;
        return (score, score - 1.25m, score + 1.25m);
    }

    // A made-up band from 1 (well below average) to 5 (well above average).
    private static string ProgressBand(decimal score, decimal lower, decimal upper) =>
        upper < 0 ? (score < -1m ? "1" : "2") : lower > 0 ? (score > 1m ? "5" : "4") : "3";

    private static string TwoPlaces(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>#939: the KS2 pupil file of September (<c>ks2-pupils</c>) or December (<c>ks2-pupils-december</c>).</summary>
public sealed class Ks2PupilsSampleGenerator(bool december) : ISampleDataGenerator
{
    public string Name => december ? "ks2-pupils-december" : "ks2-pupils";

    public IEnumerable<IReadOnlyDictionary<string, string>> Rows(SampleSchool school, SampleSchemaShape schema) =>
        Ks2SampleData.Pupils(school.Urn, school.Laestab, SchoolIndex(school.Laestab), december);
}

/// <summary>#939: the KS2 summary file (<c>ks2-summary</c>). One row has the columns of both the September and the December schema.</summary>
public sealed class Ks2SummarySampleGenerator : ISampleDataGenerator
{
    public string Name => "ks2-summary";

    public IEnumerable<IReadOnlyDictionary<string, string>> Rows(SampleSchool school, SampleSchemaShape schema) =>
        [Ks2SampleData.Summary(school.Urn, school.Laestab, SchoolIndex(school.Laestab), suppressDisadvantaged: false)];
}
