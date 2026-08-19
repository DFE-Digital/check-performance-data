using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public static class SeedCheckingWindows
{
    // A KS4-style window ingests one supplier file; a Post16 window ingests two (included +
    // non-included), so each window is seeded with the dataset slots its type requires.
    private static List<CheckingWindowDataset> DatasetsFor(CheckingWindowType type) =>
        type == CheckingWindowType.Post16
            ?
            [
                new CheckingWindowDataset { Name = "included", Included = true, SortOrder = 0 },
                new CheckingWindowDataset { Name = "nonincluded", Included = false, SortOrder = 1 }
            ]
            : [new CheckingWindowDataset { Name = "pupils", Included = null, SortOrder = 0 }];

    // A window's exercises must cover exactly its outer StartDate/EndDate — that union rule is what
    // lets the landing page keep deciding card visibility from the outer pair alone. Single-activity
    // window types get one PupilData exercise across the whole window; Post16 splits, with results
    // enquiry running far longer than pupil data checking (7 Oct - 31 Mar against 7 Oct - 18 Oct in
    // the real calendar). See docs/16-19-window-model.md.
    private static List<CheckingExercise> ExercisesFor(
        CheckingWindowType type, DateTime startDate, DateTime endDate) =>
        type == CheckingWindowType.Post16
            ?
            [
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = startDate,
                    // 14 days from a start of yesterday, which is the same fortnight the KS4
                    // windows run for. Results enquiry then carries on to the window's own end.
                    EndDate = startDate.AddDays(14).Date.AddHours(17),
                    SortOrder = 0
                },
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    StartDate = startDate,
                    EndDate = endDate,
                    SortOrder = 1
                }
            ]
            :
            [
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = startDate,
                    EndDate = endDate,
                    SortOrder = 0
                }
            ];

    public static async Task ExecuteSeed(IPortalDbContext dbContext, Guid openKs4WindowId, Guid closedKs4WindowId, Guid post16WindowId)
    {
        await dbContext.ChangeRequests.ExecuteDeleteAsync();
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        var openKs4Start = DateTime.Now.AddDays(-1);
        var openKs4End = DateTime.Now.AddDays(+13).Date.AddHours(17);

        var openKs4JuneWindow = new CheckingWindow
        {
            Id = openKs4WindowId,
            StartDate = openKs4Start,
            EndDate = openKs4End,
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "Key Stage 4 June",
            Datasets = DatasetsFor(CheckingWindowType.KS4June),
            CheckingExercises = ExercisesFor(CheckingWindowType.KS4June, openKs4Start, openKs4End)
        };

        var closedKs4Start = DateTime.Now.AddYears(-1).AddDays(-1);
        var closedKs4End = DateTime.Now.AddYears(-1).AddDays(+13).Date.AddHours(17);

        var closedKs4JuneWindow = new CheckingWindow
        {
            Id = closedKs4WindowId,
            StartDate = closedKs4Start,
            EndDate = closedKs4End,
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "KS4 June",
            Datasets = DatasetsFor(CheckingWindowType.KS4June),
            CheckingExercises = ExercisesFor(CheckingWindowType.KS4June, closedKs4Start, closedKs4End)
        };

        // The outer end date runs out to the results-enquiry exercise, because the window's dates
        // are the union of its exercises. The window is open for longer than it used to be locally;
        // that is the multi-exercise shape, and nothing reads the exercise rows yet.
        var post16Start = DateTime.Now.AddDays(-1);
        var post16End = DateTime.Now.AddDays(+180).Date.AddHours(17);

        var openPost16Window = new CheckingWindow
        {
            Id = post16WindowId,
            StartDate = post16Start,
            EndDate = post16End,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19",
            Datasets = DatasetsFor(CheckingWindowType.Post16),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, post16Start, post16End)
        };

        await dbContext.CheckingWindows.AddRangeAsync(
            openKs4JuneWindow,
            // new CheckingWindow
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddMonths(1),
            //     EndDate = DateTime.Now.AddMonths(1).AddDays(+14).Date.AddHours(17),
            //     KeyStage = KeyStages.KS4,
            //     CheckingWindowType = CheckingWindowType.KS4Autumn,
            //     Title = "KS4 Autumn"
            // },
            // new CheckingWindow
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddDays(-3),
            //     EndDate = DateTime.Now.AddDays(+11).Date.AddHours(17),
            //     KeyStage = KeyStages.KS2,
            //     CheckingWindowType = CheckingWindowType.KS2,
            //     Title = "KS2"
            // },
            // new CheckingWindow()
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddDays(-4),
            //     EndDate = DateTime.Now.AddDays(+14).Date.AddHours(17),
            //     KeyStage = KeyStages.Post16,
            //     CheckingWindowType = CheckingWindowType.Post16,
            //     Title = "16-18"
            // },
            // new CheckingWindow()
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddYears(-1).AddDays(-2),
            //     EndDate = DateTime.Now.AddYears(-1).AddDays(+12).Date.AddHours(17),
            //     KeyStage = KeyStages.Post16,
            //     CheckingWindowType = CheckingWindowType.Post16,
            //     Title = "16-18"
            // },
            closedKs4JuneWindow,
            openPost16Window
        );
        
        await dbContext.SaveChangesAsync();
    }
}