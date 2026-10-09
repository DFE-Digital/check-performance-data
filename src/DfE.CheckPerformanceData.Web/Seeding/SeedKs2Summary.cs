using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the summary data share of the Key Stage 2 window
/// (<see cref="SeedCheckingWindows.Ks2SummaryExercise"/>). Links <c>summary/summary.csv</c> to its
/// one slot with <c>ks2/summary_schema.json</c> and validates the share, so it has one release.
/// </summary>
/// <remarks>
/// The file has the December shape: every column of the schema, so the four disadvantaged pupil
/// fields that only the December file carries have values too. One row per KS2 school
/// (<see cref="Schools"/>). The
/// values are made up but stable, and agree with each other: boys and girls add up to the cohort,
/// and each percentage is its count over the cohort. A percentage has a % sign, as the
/// specification's format "999%" shows it. When there is more than one school, the last has too
/// few disadvantaged pupils to publish, so its disadvantaged percentages are suppressed ("+").
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedKs2Summary
{
    public const string File = "summary/summary.csv";
    public const string Schema = "summary_schema.json";

    /// <summary>
    /// The schools with KS2 data, the LAESTAB as the supplier sends it (no "/"). Not the KS4 and
    /// 16-19 schools of <see cref="SeedPupilData"/>: a secondary school never sees a KS2 window.
    /// Five Ways Primary School, Heath Hayes (Staffordshire).
    /// </summary>
    internal static readonly IReadOnlyList<(string Urn, string Laestab)> Schools = [("124070", "8602180")];

    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, DevDataSeeder.KeyStage2CheckingWindowId);
        var summary = window.CheckingExercises.Single(e =>
            e.ExerciseType is null && e.Name == SeedCheckingWindows.Ks2SummaryExercise);

        var schema = await System.IO.File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "ks2", Schema));
        await SeedExerciseFixtures.LinkAsync(blobs, summary,
            summary.Datasets.Single(d => d.Name == SeedCheckingWindows.Ks2SummaryDataset), Path.GetFileName(File),
            Csv(schema), Schema, schema);
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, summary);
    }

    // One row per school, with a value for every column of the schema, in schema order.
    private static byte[] Csv(string schema)
    {
        using var doc = JsonDocument.Parse(schema);
        var columns = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        var schools = Schools;
        return SeedExerciseFixtures.WriteCsv(schools.Select((school, index) =>
        {
            var values = Row(school.Urn, school.Laestab, index, suppressDisadvantaged: schools.Count > 1 && index == schools.Count - 1);
            return (IReadOnlyDictionary<string, string>)columns.ToDictionary(c => c, c => values.TryGetValue(c, out var value)
                ? value
                : throw new InvalidOperationException($"The KS2 summary seed has no value for column '{c}'."));
        }));
    }

    // Reading, writing and maths together, then reading, writing TA, maths and GPS.
    private static readonly string[] Subjects = ["RWM", "READ", "WRITTA", "MAT", "GPS"];

    /// <summary>The school's KS2 cohort. <see cref="SeedKs2PupilData"/> writes this many included
    /// pupils, so the summary and the pupil list agree.</summary>
    internal static int Cohort(int school) => 30 + school * 7;

    /// <summary>The boys in the school's cohort; the rest are girls.</summary>
    internal static int Boys(int school) => Cohort(school) / 2 + school % 3;

    private static Dictionary<string, string> Row(string urn, string laestab, int school, bool suppressDisadvantaged)
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

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    // A percentage as schools see it: a whole number with a % sign (the spec's format "999%").
    private static string Pct(int value) => $"{Number(value)}%";

    private static string Percent(int count, int of) =>
        Pct((int)Math.Round(100m * count / of, MidpointRounding.AwayFromZero));

    // A progress score with its confidence interval, in the supplier's own format:
    // READPROG + " (" + READPROG_LOWER + " to " + READPROG_UPPER + ")".
    private static string Progress(int school, int subject)
    {
        var score = ((school * 37 + subject * 13) % 41 - 20) / 10m;
        return string.Create(CultureInfo.InvariantCulture,
            $"{score:0.00} ({score - 1.25m:0.00} to {score + 1.25m:0.00})");
    }
}
