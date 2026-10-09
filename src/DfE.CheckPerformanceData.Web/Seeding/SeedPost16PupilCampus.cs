using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the pupil campus data share every 16-19 window has
/// (<see cref="SeedCheckingWindows.PupilCampusExercise"/>). The October step links
/// <c>students/campus.csv</c> to its one slot with <c>students-campus_schema.json</c> and validates
/// the share, so it has one release. The later steps keep that file.
/// </summary>
/// <remarks>
/// The file is in the supplier's own shape: every column of the data specification, headed by its
/// field reference, one row per included student. The values are made up but stable. Every school
/// has two campuses, and every fourth student is at the second.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedPost16PupilCampus
{
    public const string File = "students/campus.csv";
    public const string Schema = "students-campus_schema.json";

    /// <summary>The sample file, for the given window's included students.</summary>
    public static byte[] Csv(Guid windowId) =>
        CampusCsv(SeedPupilData.Post16Pupils(windowId).Where(p => p.Included));

    /// <summary>
    /// What an admin does when the campus file lands: links it to the share's slot and validates
    /// the share, which makes a release. A window without the share (a test window) has nothing to
    /// fill.
    /// </summary>
    internal static async Task AddAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);
        if (window.CheckingExercises.SingleOrDefault(e =>
                e.ExerciseType is null && e.Name == SeedCheckingWindows.PupilCampusExercise) is not { } campus)
            return;

        await SeedExerciseFixtures.LinkAsync(blobs, campus,
            campus.Datasets.Single(d => d.Name == SeedCheckingWindows.PupilCampusDataset), Path.GetFileName(File),
            Csv(windowId), Schema,
            await System.IO.File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "post16", Schema)));
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, campus);
    }

    /// <summary>The campus identifier of a school's second campus. The first is the DfE number and "1".</summary>
    public static string SecondCampus(string laestab) => $"{laestab}2";

    // One row per student. The attainment programme flags cycle, so a school has students in each
    // programme and some in none; a student outside a programme has no entries or points in it.
    internal static byte[] CampusCsv(IEnumerable<Post16PupilRecord> students) =>
        SeedExerciseFixtures.WriteCsv(students.Select((student, index) =>
        {
            var alev = index % 3 == 0;
            var acad = alev || index % 7 == 0;
            var tlev = index % 4 == 1;
            var agen = index % 5 == 2;
            var techCert = index % 6 == 5;
            var tlevel = index % 9 == 4;
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["CampID"] = index % 4 == 3 ? SecondCampus(student.Laestab) : $"{student.Laestab}1",
                ["CYPMD_ID"] = student.Cypmd_Id,
                ["SURNAME"] = student.Surname,
                ["FORENAMES"] = student.Firstname,
                ["Sex"] = student.Sex,
                ["DOB"] = student.DateOfBirth,
                ["AGE"] = student.Age.ToString(CultureInfo.InvariantCulture),
                ["attendance_year_0"] = "1",
                ["attendance_year_1"] = index % 2 == 0 ? "1" : "0",
                ["attendance_year_2"] = "0",
                ["LAESTAB"] = student.Laestab,
                ["URN"] = student.Urn,
                ["UKPRN"] = student.Ukprn,
                ["ULN"] = student.Uln,
                ["KS4_DISADVANTAGE"] = index % 5 == 0 ? "1" : "0",
                ["ALEV"] = Flag(alev),
                ["TOTENTS_ALEV"] = Entries(alev, index, 3),
                ["TOTPTSE_ALEV"] = Points(alev, index, 30),
                ["ACAD"] = Flag(acad),
                ["TOTENTS_ACAD"] = Entries(acad, index, 3),
                ["TOTPTSE_ACAD"] = Points(acad, index, 32),
                ["TLEV"] = Flag(tlev),
                ["TOTENTS_TLEV"] = Entries(tlev, index, 2),
                ["TOTPTSE_TLEV"] = Points(tlev, index, 28),
                ["AGEN"] = Flag(agen),
                ["TOTENTS_AGEN"] = Entries(agen, index, 2),
                ["TOTPTSE_AGEN"] = Points(agen, index, 26),
                ["TechCert"] = Flag(techCert),
                ["TOTENTS_TechCert"] = Entries(techCert, index, 1),
                ["TOTPTSE_TechCert"] = Points(techCert, index, 20),
                ["TLEVEL"] = Flag(tlevel),
                ["TOTENTS_TLEVEL"] = Entries(tlevel, index, 3),
                ["TOTPTSE_TLEVEL"] = Points(tlevel, index, 36),
                ["L3_FLAG"] = Flag(acad || agen || tlev),
                ["KS4_Year_Calc"] = Ks4Year(student.DateOfBirth).ToString(CultureInfo.InvariantCulture)
            };
        }));

    // The summer a student turns 16 in year 11: the school year runs September to August.
    public static int Ks4Year(string dateOfBirth)
    {
        var dob = DateOnly.ParseExact(dateOfBirth, "dd/MM/yyyy", CultureInfo.InvariantCulture);
        return dob.Year + (dob.Month >= 9 ? 17 : 16);
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static string Entries(bool entered, int index, int size) =>
        (entered ? size + index % 2 * 0.5m : 0m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Points(bool entered, int index, int basePoints) =>
        (entered ? basePoints + index % 11 * 1.5m : 0m).ToString("0.00", CultureInfo.InvariantCulture);
}
