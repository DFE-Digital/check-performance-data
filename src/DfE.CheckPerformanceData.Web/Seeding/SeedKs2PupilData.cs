using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the pupil data exercise of the Key Stage 2 window. Links <c>pupils.csv</c> to its one
/// slot (<see cref="SeedCheckingWindows.Ks2PupilsDataset"/>) with <c>ks2/pupils_schema.json</c> and
/// validates the exercise, so it has one release.
/// </summary>
/// <remarks>
/// The file has the supplier's column names, taken from the schema: a property read through
/// <c>x-ingress.source</c> is written under its source column (<c>PINCL_Flag</c>, <c>Sex</c>,
/// <c>SEN</c>, <c>ETHNICITY</c>), so ingress does the same renaming it will do for the real file.
/// <c>INCLUDED</c> and <c>P_INCL_DESC</c> are not written: ingress stamps them from the schema's
/// inclusion rule.
///
/// Each KS2 school (<see cref="SeedKs2Summary.Schools"/>) has <see cref="SeedKs2Summary.Cohort"/> included pupils (201), of whom
/// <see cref="SeedKs2Summary.Boys"/> are boys, so the pupil list agrees with the Summary tab. Each
/// school also has <see cref="NonIncludedPerSchool"/> non-included pupils: 202 (not at the end of
/// KS2, so no KS2 results) and 203 (excluded at the school's request, with results). The values are
/// made up but stable, and agree with each other: an outcome follows its scaled score, and a
/// progress score is the attainment score less the predicted score.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedKs2PupilData
{
    public const string File = "pupils.csv";
    public const string Schema = "pupils_schema.json";

    private const int NonIncludedPerSchool = 6;

    // Stamped by ingress from the schema's inclusion rule, never sent by the supplier.
    private static readonly HashSet<string> StampedByIngress = ["INCLUDED", "P_INCL_DESC"];

    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, DevDataSeeder.KeyStage2CheckingWindowId);
        var pupils = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.PupilData);

        var schema = await System.IO.File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "ks2", Schema));
        await SeedExerciseFixtures.LinkAsync(blobs, pupils,
            pupils.Datasets.Single(d => d.Name == SeedCheckingWindows.Ks2PupilsDataset), File, Csv(schema), Schema, schema);
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, pupils);
    }

    // One row per pupil, one column per supplier column of the schema, in schema order.
    private static byte[] Csv(string schema)
    {
        using var doc = JsonDocument.Parse(schema);
        var columns = doc.RootElement.GetProperty("properties").EnumerateObject()
            .Where(p => !StampedByIngress.Contains(p.Name))
            .Select(p => (Property: p.Name, Header: SourceColumn(p.Value) ?? p.Name))
            .ToList();

        var rows = SeedKs2Summary.Schools.SelectMany((school, index) => Pupils(school.Urn, school.Laestab, index));
        return SeedExerciseFixtures.WriteCsv(rows.Select(values =>
            (IReadOnlyDictionary<string, string>)columns.ToDictionary(c => c.Header, c => values.TryGetValue(c.Property, out var value)
                ? value
                : throw new InvalidOperationException($"The KS2 pupil seed has no value for column '{c.Property}'."))));
    }

    private static string? SourceColumn(JsonElement property) =>
        property.TryGetProperty("x-ingress", out var ingress) && ingress.TryGetProperty("source", out var source) &&
        source.ValueKind == JsonValueKind.String
            ? source.GetString()
            : null;

    private static IEnumerable<Dictionary<string, string>> Pupils(string urn, string laestab, int school)
    {
        var cohort = SeedKs2Summary.Cohort(school);
        var boys = SeedKs2Summary.Boys(school);
        for (var i = 0; i < cohort; i++)
            yield return Pupil(urn, laestab, school, i, "201", i < boys ? "M" : "F", cohort);
        for (var i = 0; i < NonIncludedPerSchool; i++)
            yield return Pupil(urn, laestab, school, cohort + i, i % 2 == 0 ? "202" : "203", i % 2 == 0 ? "F" : "M", cohort);
    }

    private static readonly string[] Ethnicities = ["WBRI", "WBRI", "WBRI", "WIRI", "WOTH", "MWBC", "AIND", "APKN", "BAFR", "BCRB", "CHNE"];
    private static readonly string[] OtherLanguages = ["OTH", "OTB"];

    private static Dictionary<string, string> Pupil(string urn, string laestab, int school, int i, string pincl,
        string sex, int cohort)
    {
        // A different name pair for every pupil of a school (7 and 400 share no factor), and a
        // different run of names in each school.
        var name = (i * 7 + school * 13) % (SeedPupilData.Firstnames.Length * SeedPupilData.Surnames.Length);
        // Year 6 in 2025/26: born from 1 September 2014 to 31 August 2015.
        var dob = new DateOnly(2014, 9, 1).AddDays(i * 37 % 365);
        // A mobile pupil joined in the last two years; everyone else joined in reception.
        var mobile = (i + 11) % cohort < 2 + school % 3;
        var entry = mobile ? new DateOnly(2024, 10, 14).AddDays(i * 13 % 200) : new DateOnly(2019, 9, 2);
        // Bands of prior attainment, rotated so they do not follow sex.
        var rotated = (i + 3) % cohort;
        var ks1Group = rotated < cohort / 5 ? 1 : rotated < cohort / 5 + cohort * 3 / 10 ? 3 : 2;
        // EHC plans, then SEN support, in the numbers the Summary tab gives.
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
            ["P_INCL"] = pincl,
            ["SURNAME"] = SeedPupilData.Surnames[name / SeedPupilData.Firstnames.Length],
            ["FORENAME"] = SeedPupilData.Firstnames[name % SeedPupilData.Firstnames.Length],
            ["SEX"] = sex,
            ["DOB"] = dob.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["ENTRYDAT"] = entry.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["SENF"] = sen,
            ["ETHNIC"] = Ethnicities[(i + school) % Ethnicities.Length],
            ["LANG1ST"] = language,
            ["EALGRP"] = eal ? "2" : "1",
            ["FSM"] = disadvantaged && i % 3 != 0 ? "1" : "0",
            ["FSM6_P"] = disadvantaged ? "1" : "0",
            ["YOUNGCARER"] = i % 23 == 9 ? "1" : "0",
            ["NEWMOBILE"] = mobile ? "1" : "0"
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

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string TwoPlaces(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
