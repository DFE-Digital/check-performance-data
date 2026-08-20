using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

// Dev-only: writes per-school pupil JSON into blob storage (container {windowId},
// blob data/{laestab}_pupils.json) so local development has pupil data now that it no
// longer lives in the database. Mirrors the data the former DB SeedPupils produced.
public static class SeedPupilData
{
    private sealed record School(string Urn, string Laestab, bool AddIncluded, bool AddNonIncluded);

    private static readonly School[] Schools =
    [
        new("136774", "933/4290", AddIncluded: true,  AddNonIncluded: true),   // Minehead Middle School
        // Westfield Academy ("137203", "933/4201") deliberately has no pupil data.
        new("101243", "301/4023", AddIncluded: false, AddNonIncluded: true),   // Eastbrook School
        new("116234", "850/2729", AddIncluded: true,  AddNonIncluded: false),  // Alderwood School
        new("142313", "860/4070", AddIncluded: true,  AddNonIncluded: true),   // Kingsmead School
        new("123312", "931/6095", AddIncluded: true,  AddNonIncluded: true),   // Abingdon School (Independent)
    ];

    // Enough pupils per tab that the pupil list pages many times over (page size 10), so the
    // GOV.UK pagination window — first/last, current ± 1, ellipses — can be exercised locally.
    private const int PupilsPerGroup = 120;

    // Non-included pupils are generated from an index shifted well clear of the included ones so
    // the two groups never share a generated name pair, UPN, Cypmd id or match ref.
    private const int NonIncludedIndexOffset = 200;

    private static readonly Guid[] WindowIds =
    [
        DevDataSeeder.KeyStage4JuneCheckingWindowId,
        DevDataSeeder.ClosedKeyStage4JuneCheckingWindowId
    ];

    public static async Task ExecuteSeedAsync(IPupilDataBlobClient client)
    {
        foreach (var windowId in WindowIds)
        foreach (var school in Schools)
        {
            var pupils = new List<PupilRecord>();

            if (school.AddIncluded)
                pupils.AddRange(GeneratePupils(PupilsPerGroup, includedPincl: true, indexOffset: 0, windowId, school));

            if (school.AddNonIncluded)
                pupils.AddRange(GeneratePupils(PupilsPerGroup, includedPincl: false, NonIncludedIndexOffset, windowId, school));

            if (pupils.Count > 0)
                await client.UploadPupilsAsync(windowId, CheckingExerciseType.PupilData, school.Laestab, pupils);
        }
    }

    /// <summary>
    /// Seeds the 16-19 window. Both populations go into ONE file per school — exactly what
    /// ingress produces after merging the supplier's two 16-19 CSVs — so the read path sees the
    /// same shape locally as it will in a real window.
    /// </summary>
    public static async Task ExecutePost16SeedAsync(IPupilDataBlobClient client)
    {
        foreach (var school in Schools)
        {
            var pupils = new List<Post16PupilRecord>();

            if (school.AddIncluded)
                pupils.AddRange(GeneratePost16Pupils(PupilsPerGroup, included: true, indexOffset: 0, school));

            if (school.AddNonIncluded)
                pupils.AddRange(GeneratePost16Pupils(PupilsPerGroup, included: false, NonIncludedIndexOffset, school));

            if (pupils.Count > 0)
                await client.UploadPupilsAsync(
                    DevDataSeeder.Post16CheckingWindowId, CheckingExerciseType.PupilData, school.Laestab, pupils);
        }
    }

    // Codes seen in the supplier's included-file sample. Post16 inclusion is decided by file of
    // origin, not by these — they are display-only.
    private static readonly int[] Post16PinclCodes = [501, 502, 505, 506];

    private static IEnumerable<Post16PupilRecord> GeneratePost16Pupils(
        int count, bool included, int indexOffset, School school) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var n = i + indexOffset;
            var dob = new DateOnly(2007, (i % 12) + 1, (i % 28) + 1);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dob.Year;
            if (dob.AddYears(age) > today) age--;

            return new Post16PupilRecord
            {
                Id = Guid.NewGuid(),
                CheckingWindowId = DevDataSeeder.Post16CheckingWindowId,
                Included = included,
                Laestab = school.Laestab,
                Firstname = Firstnames[n % Firstnames.Length],
                Surname = Surnames[(n / Firstnames.Length) % Surnames.Length],
                Sex = Sexes[i % 2],
                // The 16-19 supplier sends DOB as a timestamp string, so the seed mirrors that
                // to exercise PupilDateFormatter's timestamp branch.
                DateOfBirth = dob.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd HH:mm:ss.fffffff"),
                Age = age,
                // The non-included supplier file has no P_INCL column at all.
                Pincl = included ? Post16PinclCodes[i % Post16PinclCodes.Length] : null,
                PinclAims = included ? (i % 2 == 0 ? 503 : 504) : null,
                Cypmd_Id = $"5{(n + 1):D5}",
                Urn = school.Urn,
                Ukprn = $"1000{(i % 9) + 1:D4}",
                Uln = $"99{(n + 1):D8}",
                CampId0 = included ? string.Empty : $"C{i % 3}",
                CampId1 = included ? string.Empty : $"C{(i + 1) % 3}"
            };
        });

    private static readonly string[] Firstnames =
    [
        "Alice", "Bob", "Charlie", "Diana", "Edward", "Fiona", "George", "Hannah", "Ian", "Julia",
        "Kevin", "Laura", "Michael", "Nina", "Oscar", "Paula", "Quinn", "Rachel", "Steven", "Tina"
    ];

    private static readonly string[] Surnames =
    [
        "Smith", "Jones", "Williams", "Taylor", "Brown", "Davies", "Evans", "Wilson", "Thomas", "Roberts",
        "Johnson", "Lewis", "Walker", "Robinson", "Wood", "Thompson", "White", "Watson", "Jackson", "Harris"
    ];

    private static readonly string[] FirstLanguages = ["ENG", "ENB", "OTH", "OTB", "REF", "NOT"];

    private static readonly string[] Sexes = ["M", "F"];

    private static readonly string[] YearGroups = ["10", "11"];

    private static readonly string[] EthnicityCodes =
    [
        "WBRI", "WBRI", "WBRI", "WBRI", "WBRI",
        "WIRI", "WOTH", "MWBC", "MWBA", "MWAS",
        "AIND", "APKN", "ABAN", "BCRB", "BAFR",
        "CHNE", "OOTH", "REFU", "NOBT", "MOTH"
    ];

    private static readonly string[] SenCodes = ["N", "N", "N", "K", "E"];

    private static readonly int[] NonIncludedPinclCodes = [402, 404, 407, 408, 410, 413, 422, 430];

    private static IEnumerable<PupilRecord> GeneratePupils(int count, bool includedPincl, int indexOffset,
        Guid checkingWindowId, School school) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var n = i + indexOffset;
            var dob = new DateOnly(2010, (i % 12) + 1, (i % 28) + 1);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dob.Year;
            if (dob.AddYears(age) > today) age--;
            var pinclCodes = includedPincl ? PupilInclusion.Ks4IncludedPinclCodes : NonIncludedPinclCodes;

            return new PupilRecord
            {
                Id = Guid.NewGuid(),
                CheckingWindowId = checkingWindowId,
                Laestab = school.Laestab,
                // Firstname cycles every 20 while surname advances once per full cycle, so the
                // name pair stays unique across all 400 combinations rather than repeating every 20.
                Firstname = Firstnames[n % Firstnames.Length],
                Surname = Surnames[(n / Firstnames.Length) % Surnames.Length],
                Sex = Sexes[i % 2],
                DateOfBirth = dob.ToString("dd/MM/yyyy"),
                Age = age,
                FirstLanguage = FirstLanguages[i % FirstLanguages.Length],
                Pincl = pinclCodes[i % pinclCodes.Length],
                NewMobile = i % 5 == 0,
                ActualYearGroup = YearGroups[i % YearGroups.Length],
                Ethnicity = EthnicityCodes[n % EthnicityCodes.Length],
                SenF = SenCodes[i % SenCodes.Length],
                EntryDate = new DateTime(2021, 9, (i % 20) + 1, 0, 0, 0, DateTimeKind.Utc).ToString("dd/MM/yyyy"),
                Urn = long.Parse(school.Urn),
                Cypmd_Id = $"{(n + 1):D6}",
                MatchRef = 10000 + n,
                Upn = $"A8604070{(n + 1):D4}B"
            };
        });
}
