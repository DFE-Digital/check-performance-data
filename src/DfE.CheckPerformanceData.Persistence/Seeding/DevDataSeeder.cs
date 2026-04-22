using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public class DevDataSeeder(PortalDbContext dbContext)
{
    private static readonly Guid KS4JuneWindowId = Guid.Parse("9A2949DD-BDE8-4DD6-ADC8-B8C6966D4EC1");
    private const string SeedLaestab = "123/4567";

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

    public async Task SeedAsync()
    {
        await dbContext.Pupils.ExecuteDeleteAsync();
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        var ks4JuneCheckingWindow = new CheckingWindow
            {
                Id = Guid.Parse("9A2949DD-BDE8-4DD6-ADC8-B8C6966D4EC1"),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+13)),
                KeyStage = KeyStages.KS4,
                Title = "KS4 June",
                CheckingWindowRequestSteps =
                [
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Add,
                        StepType = CheckingWindowStepType.NewLearner,
                        Order = 0,
                        IsRequired = true,
                        Title = "New Pupil Details",
                        Explanation = "Please enter the details of the new pupil."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Add,
                        StepType = CheckingWindowStepType.FurtherDetails,
                        Order = 1,
                        IsRequired = false,
                        Title = "Further Details",
                        Explanation = "Please include any other information you think is important."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Remove,
                        StepType = CheckingWindowStepType.Date,
                        Order = 0,
                        IsRequired = true,
                        Title = "Enter a date",
                        Explanation = "Some date or other."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Remove,
                        StepType = CheckingWindowStepType.CheckBox,
                        Order = 1,
                        IsRequired = true,
                        Title = "Is this true?",
                        Explanation = "Check the box is this is true."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Remove,
                        StepType = CheckingWindowStepType.FurtherDetails,
                        Order = 2,
                        IsRequired = false,
                        Title = "Some Further Details",
                        Explanation = "This is your last chance to talk."
                    },
                ]
            };

        await dbContext.CheckingWindows.AddRangeAsync(
            ks4JuneCheckingWindow,
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+13)),
                KeyStage = KeyStages.KS4,
                Title = "KS4 Autumn"
            },
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+11)),
                KeyStage = KeyStages.KS2,
                Title = "KS2"
            },
            new CheckingWindow()
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5).AddDays(+14)),
                KeyStage = KeyStages.Post16,
                Title = "16-18"
            }
        );

        await dbContext.Pupils.AddRangeAsync(GeneratePupils(count: 15, pincl: 200, firstnameOffset: 0, surnameOffset: 0));
        await dbContext.Pupils.AddRangeAsync(GeneratePupils(count: 15, pincl: 400, firstnameOffset: 10, surnameOffset: 5));

        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<Pupil> GeneratePupils(int count, int pincl, int firstnameOffset, int surnameOffset) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var dob = new DateOnly(2010, (i % 12) + 1, (i % 28) + 1);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dob.Year;
            if (dob.AddYears(age) > today) age--;

            return new Pupil
            {
                Id = Guid.NewGuid(),
                CheckingWindowId = KS4JuneWindowId,
                Laestab = SeedLaestab,
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