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
            await dbContext.Pupils.AddRangeAsync(GeneratePupils(count: 15, pincl: 200, firstnameOffset: 0, surnameOffset: 0, checkingWindowId));
            await dbContext.Pupils.AddRangeAsync(GeneratePupils(count: 15, pincl: 400, firstnameOffset: 10, surnameOffset: 5, checkingWindowId));
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
        "English", "Polish", "Urdu", "Punjabi", "Bengali",
        "Arabic", "Somali", "Romanian", "Portuguese", "Spanish"
    ];
    
    private static readonly string[] Sexes = ["M", "F"];
    
    private static IEnumerable<Pupil> GeneratePupils(int count, int pincl, int firstnameOffset, int surnameOffset, Guid checkingWindowId) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var dob = new DateOnly(2010, (i % 12) + 1, (i % 28) + 1);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dob.Year;
            if (dob.AddYears(age) > today) age--;

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
                Pincl = pincl
            };
        });
}