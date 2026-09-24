using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

// Dev-only: generates the pupils of the fixture windows. SeedExerciseFixtures writes them as
// supplier-shaped CSVs and runs them through ingress, so each window gets a schema and a release
// exactly as an admin upload would.
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

    // Kingsmead is the school the dev impersonation flow signs in as, so it is the only school
    // whose seed carries the deliberate duplicate below.
    private const string KingsmeadLaestab = "860/4070";

    /// <summary>Every school's pupils for one KS4 window, in one list: the supplier's KS4 file is
    /// one file for every school, split into one blob per school by its LAESTAB column.</summary>
    public static IReadOnlyList<PupilRecord> Ks4Pupils(Guid windowId)
    {
        var pupils = new List<PupilRecord>();
        foreach (var school in Schools)
        {
            if (school.AddIncluded)
                pupils.AddRange(GeneratePupils(PupilsPerGroup, includedPincl: true, indexOffset: 0, windowId, school));

            if (school.AddNonIncluded)
                pupils.AddRange(GeneratePupils(PupilsPerGroup, includedPincl: false, NonIncludedIndexOffset, windowId, school));

            // Kingsmead additionally carries a deliberate same-name/DOB pair — one included, one
            // not — so the Add journey's duplicate check can be exercised in its Multiple state.
            if (windowId == DevDataSeeder.KeyStage4JuneCheckingWindowId && school.Laestab == KingsmeadLaestab)
                pupils.AddRange(GenerateDuplicateMatchPair(windowId, school));
        }
        return pupils;
    }

    /// <summary>
    /// Every school's students for one 16-19 window. The supplier sends the included and the
    /// non-included students as two files; the caller splits this list on
    /// <see cref="Post16PupilRecord.Included"/> to write them.
    /// </summary>
    public static IReadOnlyList<Post16PupilRecord> Post16Pupils(Guid windowId)
    {
        var pupils = new List<Post16PupilRecord>();
        foreach (var school in Schools)
        {
            if (school.AddIncluded)
                pupils.AddRange(GeneratePost16Pupils(PupilsPerGroup, included: true, indexOffset: 0, windowId, school));

            if (school.AddNonIncluded)
                pupils.AddRange(GeneratePost16Pupils(PupilsPerGroup, included: false, NonIncludedIndexOffset, windowId, school));
        }
        return pupils;
    }

    // Codes seen in the supplier's included-file sample. Post16 inclusion is decided by file of
    // origin, not by these — they are display-only.
    private static readonly int[] Post16PinclCodes = [501, 502, 505, 506];

    private static IEnumerable<Post16PupilRecord> GeneratePost16Pupils(
        int count, bool included, int indexOffset, Guid checkingWindowId, School school) =>
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
                CheckingWindowId = checkingWindowId,
                Included = included,
                // The supplier's 16-19 file writes LAESTAB without the slash, and its schema caps
                // the column at seven characters.
                Laestab = school.Laestab.Replace("/", string.Empty),
                Firstname = Firstnames[n % Firstnames.Length],
                Surname = Surnames[(n / Firstnames.Length) % Surnames.Length],
                Sex = Sexes[i % 2],
                // dd/MM/yyyy, as the supplier's 16-19 CSV writes it; its schema caps DOB at ten
                // characters.
                DateOfBirth = dob.ToString("dd/MM/yyyy"),
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

    // A name/DOB pair deliberately shared by two pupils for Kingsmead's live KS4June window. The
    // generator's name pairs are unique by construction (included surnames bucket 0-5, non-included
    // bucket 10-15), so without this the duplicate check could never reach its Multiple state. The
    // name is drawn from outside Firstnames/Surnames so it cannot collide with a generated pupil,
    // and one copy is Pincl-included while the other is not — giving the Multiple table one row of
    // each status, and therefore a "Switch to include" button for the non-included pupil.
    private static IEnumerable<PupilRecord> GenerateDuplicateMatchPair(Guid checkingWindowId, School school)
    {
        const string firstname = "Casey";
        const string surname = "Carter";
        const string dob = "15/03/2010";

        yield return new PupilRecord
        {
            Id = Guid.NewGuid(),
            CheckingWindowId = checkingWindowId,
            Laestab = school.Laestab,
            Firstname = firstname,
            Surname = surname,
            Sex = "F",
            DateOfBirth = dob,
            Age = 16,
            FirstLanguage = "ENG",
            Pincl = PupilInclusion.Ks4IncludedPinclCodes[0],
            NewMobile = false,
            ActualYearGroup = "11",
            Ethnicity = "WOTH",
            SenF = "N",
            EntryDate = "01/09/2021",
            Urn = long.Parse(school.Urn),
            Cypmd_Id = "800001",
            MatchRef = 80001,
            Upn = "A8604078001B"
        };

        yield return new PupilRecord
        {
            Id = Guid.NewGuid(),
            CheckingWindowId = checkingWindowId,
            Laestab = school.Laestab,
            Firstname = firstname,
            Surname = surname,
            Sex = "F",
            DateOfBirth = dob,
            Age = 16,
            FirstLanguage = "ENG",
            Pincl = NonIncludedPinclCodes[0],
            NewMobile = false,
            ActualYearGroup = "11",
            Ethnicity = "WOTH",
            SenF = "N",
            EntryDate = "01/09/2021",
            Urn = long.Parse(school.Urn),
            Cypmd_Id = "800002",
            MatchRef = 80002,
            Upn = "A8604078002B"
        };
    }

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
