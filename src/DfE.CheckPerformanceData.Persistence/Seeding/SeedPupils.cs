using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public static class SeedPupils
{
    public static async Task ExecuteSeed(IPortalDbContext dbContext, Guid[] checkingWindowIds)
    {
        await dbContext.Pupils.ExecuteDeleteAsync();

        foreach (var checkingWindowId in checkingWindowIds)
        {
            await dbContext.Pupils.AddRangeAsync(GeneratePupils(count: 15, includedPincl: true, firstnameOffset: 0, surnameOffset: 0, checkingWindowId));
            await dbContext.Pupils.AddRangeAsync(GeneratePupils(count: 15, includedPincl: false, firstnameOffset: 10, surnameOffset: 5, checkingWindowId));
        }

        await dbContext.SaveChangesAsync();
    }
    
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

    private static readonly string[] FirstLanguages =
    [
        "ENG", "ENB", "OTH", "OTB", "REF", "NOT"
    ];
    
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

    private static readonly int[] IncludedPinclCodes = [401, 403, 414, 421, 431];
    private static readonly int[] NonIncludedPinclCodes = [402, 404, 407, 408, 410, 413, 422, 430];

    private static IEnumerable<Pupil> GeneratePupils(int count, bool includedPincl, int firstnameOffset, int surnameOffset, Guid checkingWindowId) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var dob = new DateOnly(2010, (i % 12) + 1, (i % 28) + 1);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dob.Year;
            if (dob.AddYears(age) > today) age--;
            var pinclCodes = includedPincl ? IncludedPinclCodes : NonIncludedPinclCodes;

            return new Pupil
            {
                Id = Guid.NewGuid(),
                CheckingWindowId = checkingWindowId,
                Laestab = "8604070",
                Firstname = Firstnames[(i + firstnameOffset) % Firstnames.Length],
                Surname = Surnames[(i + surnameOffset) % Surnames.Length],
                Sex = Sexes[i % 2],
                DateOfBirth = dob.ToString("dd/MM/yyyy"),
                Age = age,
                FirstLanguage = FirstLanguages[i % FirstLanguages.Length],
                Pincl = pinclCodes[i % pinclCodes.Length],
                NewMobile = i % 5 == 0,
                ActualYearGroup = YearGroups[i % YearGroups.Length],
                Ethnicity = EthnicityCodes[(i + firstnameOffset) % EthnicityCodes.Length],
                SenF = SenCodes[i % SenCodes.Length],
                EntryDate = new DateTime(2021, 9, (i % 20) + 1, 0, 0, 0, DateTimeKind.Utc),
                Urn = "142313",
                Cypmd_Id = $"CYPMD{(i + firstnameOffset + 1):D6}",
                MatchRef = 10000 + i + firstnameOffset,
                Upn = $"A8604070{(i + firstnameOffset + 1):D4}B"
            };
        });
}