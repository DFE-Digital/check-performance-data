using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
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
        CheckingWindowType type, DateTime startDate, DateTime endDate, DateTime? pupilDataEnd = null,
        IReadOnlyList<string>? resultsFiles = null) =>
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
                    // 14 days from a start of yesterday, which is the same fortnight the KS4
                    // windows run for, unless the caller wants pupil data to have shut already.
                    // Results enquiry then carries on to the window's own end.
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
                    Datasets = ResultsDatasetsFor(resultsFiles ?? DefaultResultsFiles)
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

    // The results files the E2E fixture windows hold: SeedStudentResults writes rows from the main
    // file and the first late file, and deliberately none from the second late file.
    private static readonly string[] DefaultResultsFiles = [ResultsFileTags.Post16Main, ResultsFileTags.Post16LateResults1];

    // One slot per results file that has landed, named by the tag it stamps, as the admin wizard
    // names them. Only the main file is required.
    private static List<CheckingWindowDataset> ResultsDatasetsFor(IReadOnlyList<string> resultsFiles) =>
        resultsFiles.Select((tag, index) => new CheckingWindowDataset
        {
            Name = tag, SourceFile = tag, Included = null, Required = tag == ResultsFileTags.Post16Main,
            FeedsJourney = true, SortOrder = index
        }).ToList();

    public static async Task ExecuteSeed(IPortalDbContext dbContext, Guid openKs4WindowId, Guid closedKs4WindowId, Guid post16WindowId, Guid closedPupilDataPost16WindowId)
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

        // The outer end date runs out to the results-enquiry exercise, because the window's dates
        // are the union of its exercises. The window is open for longer than it used to be locally;
        // that is the multi-exercise shape, and nothing reads the exercise rows yet.
        var post16Start = DateTime.Now.AddDays(-1);
        var post16End = DateTime.Now.AddDays(+180).Date.AddHours(17);
        var nextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1);

        var openPost16Window = new CheckingWindow
        {
            Id = post16WindowId,
            StartDate = post16Start,
            EndDate = post16End,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = nextOpportunity,
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, post16Start, post16End)
        };

        // AB#298317: pupil data checking shut yesterday; results enquiry runs on for months. The
        // outer pair is the union of the two, as for every window.
        var closedPost16Start = DateTime.Now.AddDays(-30);
        var closedPost16PupilDataEnd = DateTime.Now.AddDays(-1).Date.AddHours(17);
        var closedPost16End = DateTime.Now.AddDays(+180).Date.AddHours(17);

        var closedPupilDataPost16Window = new CheckingWindow
        {
            Id = closedPupilDataPost16WindowId,
            StartDate = closedPost16Start,
            EndDate = closedPost16End,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 (pupil data closed)",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = nextOpportunity,
            CheckingExercises = ExercisesFor(
                CheckingWindowType.Post16, closedPost16Start, closedPost16End,
                pupilDataEnd: closedPost16PupilDataEnd)
        };

        // Two windows ingested from the files under Data/Ingress: one at the start of the Autumn
        // window today, and one seen in February, four months in.
        var ingressStart = DateTime.Today;
        var ingressEnd = ingressStart.AddMonths(1).AddHours(17);
        var post16IngressWindow = IngestedPost16Window(
            DevDataSeeder.Post16IngressCheckingWindowId, "16 to 19 ingress",
            start: ingressStart, pupilDataEnd: ingressEnd, end: ingressEnd,
            currentSummary: 0, resultsFiles: [ResultsFileTags.Post16Main]);
        var februaryStart = DateTime.Today.AddMonths(-4);
        var post16FebruaryWindow = IngestedPost16Window(
            DevDataSeeder.Post16FebruaryCheckingWindowId, "16 to 19 February",
            start: februaryStart, pupilDataEnd: februaryStart.AddDays(14).AddHours(17), end: DateTime.Today.AddMonths(2).AddHours(17),
            currentSummary: 2, resultsFiles: [ResultsFileTags.Post16Main, ResultsFileTags.Post16LateResults1]);

        await dbContext.CheckingWindows.AddRangeAsync(
            post16IngressWindow,
            post16FebruaryWindow,
            openKs4JuneWindow, 
            closedKs4JuneWindow,
            openPost16Window,
            closedPupilDataPost16Window
        );
        
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// A 16-19 window whose exercises are ingested from the files under Data/Ingress by
    /// SeedPost16Ingress. Pupil data checking runs a fortnight from the start and results enquiry
    /// to the end, so a start in the past stages a window where only enquiry is still open.
    /// </summary>
    /// <param name="currentSummary">Which of the four Summary exercises is enabled (0 = Autumn CE).</param>
    /// <param name="resultsFiles">The results files that have landed, as <see cref="ResultsFileTags"/>.</param>
    private static CheckingWindow IngestedPost16Window(Guid id, string title, DateTime start, DateTime pupilDataEnd,
        DateTime end, int currentSummary, IReadOnlyList<string> resultsFiles)
    {
        var exercises = ExercisesFor(CheckingWindowType.Post16, start, end, pupilDataEnd, resultsFiles);

        // The Summary exercises sort first on this window.
        var students = exercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
        students.SortOrder = 1;

        var results = exercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        results.SortOrder = 2;

        // The Summary tab is one record per school, pivoted on the page (the exercise's layout is
        // Vertical). Over the year it is four exercises, each replacing the last, and
        // the supplier's file has a different column set for each (62, 102, 102 and 135 columns),
        // so each dataset names the schema for its own shape. Display-only, so no journey or close
        // action. One is enabled: an admin enables the next to swap the tab.
        CheckingExercise? previous = null;
        foreach (var ((name, dataset), index) in new[]
        {
            ("Autumn CE", "summary-autumn"),
            ("Provisional value added", "summary-november-va"),
            ("Light touch revised data share", "summary-november-va"),
            ("Retention", "summary-retention")
        }.Select((summary, index) => (summary, index)))
        {
            var summary = new CheckingExercise
            {
                Id = Guid.NewGuid(),
                Name = name,
                TabName = "Summary",
                TabOrder = 100,
                SortOrder = 0,
                StartDate = start,
                EndDate = end,
                IsEnabled = index == currentSummary,
                DisplayOnly = true,
                Layout = ExerciseLayout.Vertical,
                UsesExerciseStorage = true,
                ReplacesCheckingExerciseId = previous?.Id,
                Datasets = [new CheckingWindowDataset { Name = dataset, Included = null, Required = true, SortOrder = 0 }]
            };
            exercises.Add(summary);
            previous = summary;
        }

        return new CheckingWindow
        {
            Id = id,
            StartDate = start,
            EndDate = end,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = title,
            TurnaroundCommitment = "updated in Spring",
            CheckingExercises = exercises
        };
    }
}
