using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the value added data every 16-19 window's pupil data exercise shares from November
/// (<see cref="SeedCheckingWindows.ValueAddedDatasets"/>). The slots feed no journey. Each step
/// fills its own slot, makes it required, retires the slot before it and validates pupil data, so
/// each step makes a release. Each step has its own schema, whose section is the heading schools
/// see. The three files have the same columns.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>November, "Value Added": <c>students/value-added.csv</c> → <c>students-value-added_schema.json</c></item>
/// <item>February, "Value Added: revised": <c>students/value-added-revised.csv</c> →
/// <c>students-value-added-revised_schema.json</c></item>
/// <item>March, "Value Added: revised incl. retention": <c>students/value-added-revised-retention.csv</c> →
/// <c>students-value-added-revised-retention_schema.json</c></item>
/// </list>
/// <para>
/// Each file has one row per included student per qualification, in the supplier's value added
/// shape: every column of the data specification, headed by its field reference. Every third
/// student has two qualifications. The revised files change the actual points, and so the value
/// added score, of some rows.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedPost16ValueAdded
{
    /// <summary>One step's value added file: its slot, sample file, schema and revision (0 for November).</summary>
    public sealed record Step(string Slot, string File, string Schema, int Revision);

    public static readonly Step November = new(SeedCheckingWindows.ValueAddedDatasets[0],
        "students/value-added.csv", "students-value-added_schema.json", Revision: 0);

    public static readonly Step February = new(SeedCheckingWindows.ValueAddedDatasets[1],
        "students/value-added-revised.csv", "students-value-added-revised_schema.json", Revision: 1);

    public static readonly Step March = new(SeedCheckingWindows.ValueAddedDatasets[2],
        "students/value-added-revised-retention.csv", "students-value-added-revised-retention_schema.json", Revision: 2);

    public static readonly IReadOnlyList<Step> Steps = [November, February, March];

    /// <summary>The step's sample file, for the given window's included students.</summary>
    public static byte[] Csv(Step step, Guid windowId) =>
        ValueAddedCsv(SeedPupilData.Post16Pupils(windowId).Where(p => p.Included), step.Revision);

    /// <summary>
    /// What an admin does when a value added file lands: links it to its pupil data slot, makes the
    /// slot required, retires the slot before it and validates pupil data, which makes a new
    /// release. A window without the slot (a test window) has nothing to fill.
    /// </summary>
    internal static async Task AddAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId, Step step)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);
        var pupilData = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.PupilData);
        if (pupilData.Datasets.SingleOrDefault(d => d.Name == step.Slot) is not { } dataset)
            return;

        await SeedExerciseFixtures.LinkAsync(blobs, pupilData, dataset, Path.GetFileName(step.File),
            Csv(step, windowId), step.Schema,
            await File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "post16", step.Schema)));
        dataset.Required = true;
        var index = Steps.ToList().IndexOf(step);
        if (index > 0)
            pupilData.Datasets.Single(d => d.Name == Steps[index - 1].Slot).Retired = true;
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, pupilData);
    }

    // A value added data file in the supplier's own shape. A row's actual points move one grade
    // (10 points) up in the revised file for every fifth row, and down again in the retention file
    // for every seventh row.
    internal static byte[] ValueAddedCsv(IEnumerable<Post16PupilRecord> students, int revision) =>
        SeedExerciseFixtures.WriteCsv(students.SelectMany((student, index) =>
            Enumerable.Range(0, index % 3 == 0 ? 2 : 1).Select(n =>
            {
                var row = index + n;
                var qualification = Qualifications[row % Qualifications.Length];
                var priorAttainment = 4 + row % 5;
                var estimated = Math.Round(qualification.Size * (18.5m + priorAttainment * 3.1m + row % 7 * 0.41m), 5);
                var actual = qualification.Size * (10 * (2 + row % 5))
                    + (revision >= 1 && row % 5 == 0 ? 10 : 0)
                    - (revision >= 2 && row % 7 == 0 ? 10 : 0);
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["Forename"] = student.Firstname,
                    ["Surname"] = student.Surname,
                    ["Sex"] = student.Sex,
                    ["Qualification_code"] = qualification.Code.ToString(CultureInfo.InvariantCulture),
                    ["Qualification_name"] = qualification.Name,
                    ["Subject_code"] = qualification.SubjectCode.ToString(CultureInfo.InvariantCulture),
                    ["Subject_name"] = qualification.Subject,
                    ["Size"] = qualification.Size.ToString(CultureInfo.InvariantCulture),
                    ["Cohort"] = qualification.Cohort,
                    ["Prior_attainment"] = priorAttainment.ToString(CultureInfo.InvariantCulture),
                    ["Estimated_points"] = estimated.ToString(CultureInfo.InvariantCulture),
                    ["Actual_points"] = actual.ToString(CultureInfo.InvariantCulture),
                    ["Value_Added_score"] = (actual - estimated).ToString(CultureInfo.InvariantCulture),
                    ["QUAL_ID"] = $"{qualification.Code}{qualification.SubjectCode}1",
                    ["Disadvantaged_status"] = (row % 4 == 0 ? 1 : 0).ToString(CultureInfo.InvariantCulture),
                    ["CYPMD_ID"] = student.Cypmd_Id,
                    ["Laestab"] = student.Laestab
                };
            })));

    private static readonly (int Code, string Name, int SubjectCode, string Subject, decimal Size, string Cohort)[] Qualifications =
    [
        (111, "GCE A level", 13690, "Art & Design (Fine Art)", 1, "A level"),
        (111, "GCE A level", 20330, "Mathematics", 1, "A level"),
        (111, "GCE A level", 50710, "English Literature", 1, "A level"),
        (111, "GCE A level", 17020, "Biology", 1, "A level"),
        (130, "GCE AS level", 21020, "Further Mathematics", 0.5m, "A level"),
        (500, "BTEC Extended Diploma", 69030, "Sport", 3, "Applied general")
    ];
}
