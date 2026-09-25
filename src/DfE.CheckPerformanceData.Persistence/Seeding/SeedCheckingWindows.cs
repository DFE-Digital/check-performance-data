using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public static class SeedCheckingWindows
{
    // A KS4-style window ingests one supplier file; a Post16 window ingests two (included +
    // non-included), so each pupil-data exercise is seeded with the dataset slots its window type
    // requires. The results-enquiry exercise reads the school results file and has no slots.
    private static List<CheckingWindowDataset> DatasetsFor(CheckingWindowType type) =>
        type == CheckingWindowType.Post16
            ?
            [
                new CheckingWindowDataset { Name = "included", Included = true, FeedsJourney = true, SortOrder = 0 },
                new CheckingWindowDataset { Name = "nonincluded", Included = false, FeedsJourney = true, SortOrder = 1 }
            ]
            : [new CheckingWindowDataset { Name = "pupils", Included = null, FeedsJourney = true, SortOrder = 0 }];

    // A window's exercises must cover exactly its outer StartDate/EndDate — that union rule is what
    // lets the landing page keep deciding card visibility from the outer pair alone. Single-activity
    // window types get one PupilData exercise across the whole window; Post16 splits, with results
    // enquiry running far longer than pupil data checking (7 Oct - 31 Mar against 7 Oct - 18 Oct in
    // the real calendar). See docs/16-19-window-model.md.
    //
    // Every seeded exercise is new style: named, enabled and on a tab, so the Check Your Pupil
    // Data page draws exercise tabs for every seeded window.
    private static List<CheckingExercise> ExercisesFor(
        CheckingWindowType type, DateTime startDate, DateTime endDate, DateTime? pupilDataEnd = null) =>
        type == CheckingWindowType.Post16
            ?
            [
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    Name = "Pupil data checking",
                    TabName = "Students",
                    TabOrder = 200,
                    IsEnabled = true,
                    StartDate = startDate,
                    // A fortnight from the start unless the caller sets it. Results enquiry
                    // then carries on to the window's own end.
                    EndDate = pupilDataEnd ?? startDate.AddDays(14).Date.AddHours(17),
                    SortOrder = 0,
                    Datasets = DatasetsFor(CheckingWindowType.Post16)
                },
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    Name = "Results enquiry",
                    TabName = "Results",
                    TabOrder = 300,
                    IsEnabled = true,
                    StartDate = startDate,
                    EndDate = endDate,
                    SortOrder = 1,
                    Datasets = ResultsDatasets()
                }
            ]
            :
            [
                new CheckingExercise
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    Name = "Pupil data checking",
                    TabName = "Pupils",
                    TabOrder = 200,
                    IsEnabled = true,
                    // KS4 sends one pupils file; each pupil's own P_INCL puts them on the
                    // "Pupils Included" or the "Pupils Non Included" tab.
                    Layout = ExerciseLayout.InclusionTabs,
                    StartDate = startDate,
                    EndDate = endDate,
                    SortOrder = 0,
                    Datasets = DatasetsFor(type)
                }
            ];

    // Every slot of the year, as the admin wizard creates them (WindowDatasets.DefaultsFor). All
    // start empty: the admin fills each one when its file arrives.
    private static List<CheckingWindowDataset> ResultsDatasets() =>
        WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry)
            .Select(d => new CheckingWindowDataset
            {
                Name = d.Name, SourceFile = d.SourceFile, Included = null, Required = d.Required,
                FeedsJourney = true, SortOrder = d.SortOrder
            }).ToList();

    public static async Task ExecuteSeed(IPortalDbContext dbContext, Guid openKs4WindowId, Guid closedKs4WindowId,
        Guid post16OctoberWindowId, Guid post16NovemberWindowId)
    {
        // Egress runs first: egress_runs → CheckingWindows is a RESTRICT foreign key (an egress
        // is an audit record and must never vanish because a window was deleted), so a run left
        // behind — an E2E cleanup that failed part-way is enough — made the window wipe below
        // throw, the host terminated before it listened, and the review app's new pod never became
        // Ready while the old one kept serving. Outputs and learner rows cascade from the run.
        await dbContext.EgressRuns.ExecuteDeleteAsync();
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
            TurnaroundCommitment = "updated in the Autumn",
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
            TurnaroundCommitment = "updated in the Autumn",
            CheckingExercises = ExercisesFor(CheckingWindowType.KS4June, closedKs4Start, closedKs4End)
        };

        // "16 to 19 Oct": the start of the 16-19 results enquiry. It opens today with pupil data
        // checking for a fortnight (7 to 18 October in the real calendar) and the results enquiry
        // to the end of March. Both exercises are enabled and have their dataset slots — two
        // student files, and a results slot for every file of the year — but no data: an admin
        // imports the October files (included, non-included and late results 1) from the sample
        // files in ingress storage and validates. The outer dates are the union of the exercises.
        var octoberStart = DateTime.Today;
        var octoberPupilDataEnd = octoberStart.AddDays(11).AddHours(17);
        var octoberEnd = new DateTime(octoberStart.Month > 3 ? octoberStart.Year + 1 : octoberStart.Year, 3, 31, 17, 0, 0);

        var post16OctoberWindow = new CheckingWindow
        {
            Id = post16OctoberWindowId,
            StartDate = octoberStart,
            EndDate = octoberEnd,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 Oct",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, octoberStart, octoberEnd, pupilDataEnd: octoberPupilDataEnd)
        };

        // "16 to 19 Nov": the same exercises, slots and dates as October. The Web seed then imports
        // and validates the October files into it (SeedPost16NovemberSamples), so only the late
        // results 2 slot is empty: an admin adds that file to test the November release.
        var post16NovemberWindow = new CheckingWindow
        {
            Id = post16NovemberWindowId,
            StartDate = octoberStart,
            EndDate = octoberEnd,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 Nov",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, octoberStart, octoberEnd, pupilDataEnd: octoberPupilDataEnd)
        };

        await dbContext.CheckingWindows.AddRangeAsync(
            openKs4JuneWindow,
            closedKs4JuneWindow,
            post16OctoberWindow,
            post16NovemberWindow
        );
        
        await dbContext.SaveChangesAsync();
    }
}
