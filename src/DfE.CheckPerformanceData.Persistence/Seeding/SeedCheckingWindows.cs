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

    /// <summary>The name of every 16-19 window's pupil data slot for the previously published data.</summary>
    public const string PreviouslyPublishedDataset = "previously-published";

    /// <summary>The name of every 16-19 window's pupil data slot for the previously published revised
    /// data. It is empty and not required until the February step fills it.</summary>
    public const string PreviouslyPublishedRevisedDataset = "previously-published-revised";

    // The previously published student data, shared with schools in the pupil data exercise, and
    // the revised file that replaces it in February. Both are display only: the slots feed no
    // journey, so they stay out of the journeys' pupils file. The revised slot waits empty, like
    // the results enquiry's revised slots, so it is not required: a required empty slot would
    // stop the October run.
    private static CheckingWindow WithPreviouslyPublishedSlots(CheckingWindow window)
    {
        var datasets = window.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
            .Datasets;
        datasets.Add(new CheckingWindowDataset
        {
            Name = PreviouslyPublishedDataset, Included = null, FeedsJourney = false, SortOrder = 2
        });
        datasets.Add(new CheckingWindowDataset
        {
            Name = PreviouslyPublishedRevisedDataset, Included = null, FeedsJourney = false, Required = false,
            SortOrder = 3
        });
        return window;
    }

    /// <summary>Every 16-19 window's pupil data slots for the value added data, in the order the
    /// year fills them. Each file replaces the one before it: November's value added, February's
    /// revised and March's revised with retention.</summary>
    public static readonly IReadOnlyList<string> ValueAddedDatasets =
    [
        "Value Added",
        "Value Added: revised",
        "Value Added: revised incl. retention"
    ];

    // The value added data, shared with schools in the pupil data exercise from November. Like the
    // previously published slots, the slots are display only and feed no journey. All three wait
    // empty and are not required: a required empty slot would stop the October run. Each step
    // fills its slot, makes it required and retires the one before it.
    private static CheckingWindow WithValueAddedSlots(CheckingWindow window)
    {
        var datasets = window.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
            .Datasets;
        datasets.AddRange(ValueAddedDatasets.Select((name, index) => new CheckingWindowDataset
        {
            Name = name, Included = null, FeedsJourney = false, Required = false, SortOrder = 4 + index
        }));
        return window;
    }

    /// <summary>The name of the "16 to 19 Mar" window's pupil data slot for the pupil aims data.</summary>
    public const string AimsDataset = "Aims";

    // The pupil aims data, shared with schools in the pupil data exercise of the "16 to 19 Mar"
    // window only. Like the value added slots, the slot is display only and feeds no journey. It
    // waits empty and is not required, because the earlier steps the March seed does first run
    // pupil data before the aims file arrives. The March step fills it and makes it required.
    private static CheckingWindow WithAimsSlot(CheckingWindow window)
    {
        window.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
            .Datasets.Add(new CheckingWindowDataset
            {
                Name = AimsDataset, Included = null, FeedsJourney = false, Required = false,
                SortOrder = 4 + ValueAddedDatasets.Count
            });
        return window;
    }

    /// <summary>The name of the summary share's exercise.</summary>
    public const string SummaryExercise = "Summary";

    /// <summary>The summary share's slots, in the order the year fills them. Each file replaces
    /// the one before it: October's summary, November's value added, February's revised value
    /// added and March's revised value added with retention.</summary>
    public static readonly IReadOnlyList<string> SummaryDatasets =
    [
        "Summary",
        "Summary with value added",
        "Summary with value added: revised",
        // "revised including retention" would not fit: a slot name is at most 50 characters.
        "Summary with value added: revised incl. retention"
    ];

    // A display-only data share with no kind, in every 16-19 window. Each
    // school has one summary row, so it shows as label/value rows. Only the October slot is
    // required: the later slots wait empty, because a required empty slot would stop the October
    // run. Each step fills its slot, makes it required and retires the one before it. Its tab is
    // first, before Students.
    private static CheckingExercise SummaryDataShare(DateTime startDate, DateTime endDate) => new()
    {
        ExerciseType = null,
        DisplayOnly = true,
        Name = SummaryExercise,
        TabName = "Summary",
        TabOrder = 100,
        IsEnabled = true,
        Layout = ExerciseLayout.Vertical,
        StartDate = startDate,
        EndDate = endDate,
        Datasets = SummaryDatasets.Select((name, index) => new CheckingWindowDataset
        {
            Name = name, Included = null, FeedsJourney = false, Required = index == 0, SortOrder = index
        }).ToList()
    };

    // Every 16-19 window has the summary share. The October step fills its first slot; each later
    // step replaces the file before it.
    private static CheckingWindow WithSummaryDataShare(CheckingWindow window)
    {
        var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        window.CheckingExercises.Add(SummaryDataShare(results.StartDate, results.EndDate));
        return window;
    }

    /// <summary>The name of the pupil campus share's exercise.</summary>
    public const string PupilCampusExercise = "Pupil campus";

    /// <summary>The name of the pupil campus share's one dataset slot.</summary>
    public const string PupilCampusDataset = "campus";

    // A display-only data share with no kind, in every 16-19 window: one
    // slot that feeds no journey. The October step fills it, and the later steps keep that file.
    private static CheckingExercise PupilCampusDataShare(DateTime startDate, DateTime endDate) => new()
    {
        ExerciseType = null,
        DisplayOnly = true,
        Name = PupilCampusExercise,
        TabName = "Campus",
        TabOrder = 500,
        IsEnabled = true,
        StartDate = startDate,
        EndDate = endDate,
        Datasets = [new CheckingWindowDataset { Name = PupilCampusDataset, Included = null, FeedsJourney = false, SortOrder = 0 }]
    };

    // Every 16-19 window has the pupil campus share, on the results enquiry's dates.
    private static CheckingWindow WithPupilCampusDataShare(CheckingWindow window)
    {
        var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        window.CheckingExercises.Add(PupilCampusDataShare(results.StartDate, results.EndDate));
        return window;
    }

    public static async Task ExecuteSeed(IPortalDbContext dbContext, Guid openKs4WindowId, Guid post16OctoberWindowId, Guid post16NovemberWindowId, Guid post16FebruaryWindowId,
        Guid post16MarchWindowId)
    {
        // Egress runs first: egress_runs → CheckingWindows is a RESTRICT foreign key (an egress
        // is an audit record and must never vanish because a window was deleted), so a run left
        // behind — an E2E cleanup that failed part-way is enough — made the window wipe below
        // throw, the host terminated before it listened, and the review app's new pod never became
        // Ready while the old one kept serving. Outputs and learner rows cascade from the run.
        await dbContext.EgressRuns.ExecuteDeleteAsync();
        await dbContext.ChangeRequests.ExecuteDeleteAsync();
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        var openKs4Start = DateTime.Today;
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

        // Schools keep a read-only view of their pupil data for a month after pupil data checking
        // closes. KS4 June has one exercise, so without VisibleUntil the window leaves the landing
        // page at the moment the exercise closes.
        openKs4JuneWindow.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
            .VisibleUntil = openKs4End.AddMonths(1);

        // "16 to 19 Oct": the start of the 16-19 results enquiry. It opens today with pupil data
        // checking for a fortnight (7 to 18 October in the real calendar) and the results enquiry
        // to the end of March. Both exercises are enabled and have their dataset slots — two
        // student files and the previously published file, and a results slot for every file of
        // the year — but no data here: the Web seed imports and validates the October files
        // (students, previously published, included, non-included and late results 1) with
        // SeedPost16OctoberSamples. The outer dates are the union of the exercises.
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

        // October's late results 2 has not arrived yet, so its results enquiry shows the late
        // results warning. The later windows have it, so theirs do not.
        post16OctoberWindow.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry)
            .ShowLateResultsWarning = true;

        // TEMPORARY: a copy of the October window that the Web seed ingests with the October files.
        // It is the only seeded 16-19 window. It borrows the November window's id, because the
        // November window is not seeded.
        var post16PostIngressWindow = new CheckingWindow
        {
            Id = post16NovemberWindowId,
            StartDate = octoberStart,
            EndDate = octoberEnd,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 Data",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, octoberStart, octoberEnd, pupilDataEnd: octoberPupilDataEnd)
        };
        post16PostIngressWindow.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry)
            .ShowLateResultsWarning = true;

        // The later 16-19 windows are past pupil data checking. They opened three weeks ago, pupil
        // data checking shut ten days ago, and the results enquiry runs to the end of March. The
        // Students tab still shows its data: the exercise is enabled with no VisibleUntil, so it
        // stays live, but its dates have passed, so a school cannot request a change or confirm.
        // This is how an admin keeps pupil data on view after checking shuts: set the exercise's
        // end date, and do not hide the exercise.
        var laterStart = octoberStart.AddDays(-21);
        var laterPupilDataEnd = laterStart.AddDays(11).AddHours(17);

        // "16 to 19 Nov": the same exercises and slots as October, with pupil data checking shut.
        // The Web seed does the October import, then adds and validates late results 2
        // (SeedPost16NovemberSamples).
        var post16NovemberWindow = new CheckingWindow
        {
            Id = post16NovemberWindowId,
            StartDate = laterStart,
            EndDate = octoberEnd,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 Nov",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, laterStart, octoberEnd, pupilDataEnd: laterPupilDataEnd)
        };

        // "16 to 19 Feb": the same exercises, slots and dates as November. The Web seed does the
        // November steps, then adds the revised files, retires the four files they replace and
        // validates (SeedPost16FebruarySamples): an October, a November and a February release. It
        // also fills the previously published revised slot, makes it required, retires previously
        // published and validates pupil data.
        var post16FebruaryWindow = new CheckingWindow
        {
            Id = post16FebruaryWindowId,
            StartDate = laterStart,
            EndDate = octoberEnd,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 Feb",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, laterStart, octoberEnd, pupilDataEnd: laterPupilDataEnd)
        };

        // "16 to 19 Mar": the same again, plus pupil data's aims slot. The Web seed does the
        // February steps, then adds included revised with retention, retires included revised and
        // validates, and fills the aims slot and validates pupil data (SeedPost16MarchSamples).
        var post16MarchWindow = new CheckingWindow
        {
            Id = post16MarchWindowId,
            StartDate = laterStart,
            EndDate = octoberEnd,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            Title = "16 to 19 Mar",
            TurnaroundCommitment = "updated in the Spring",
            NextOpportunity = new DateTime(DateTime.Now.Year + 1, 10, 1),
            CheckingExercises = ExercisesFor(CheckingWindowType.Post16, laterStart, octoberEnd, pupilDataEnd: laterPupilDataEnd)
        };

        await dbContext.CheckingWindows.AddRangeAsync(
            openKs4JuneWindow,
            // Every 16-19 window has both previously published slots, the three value added slots,
            // the summary share and the pupil campus share. The October step fills the first
            // previously published slot, the first summary slot and the campus slot; later steps
            // fill the others. November fills the first value added slot.
            WithPupilCampusDataShare(WithSummaryDataShare(WithValueAddedSlots(WithPreviouslyPublishedSlots(post16PostIngressWindow))))
            // TEMPORARY: only the ingested copy of the October window is seeded. Put these back to
            // restore the others.
            // WithPupilCampusDataShare(WithSummaryDataShare(WithValueAddedSlots(WithPreviouslyPublishedSlots(post16OctoberWindow)))),
            // WithPupilCampusDataShare(WithSummaryDataShare(WithValueAddedSlots(WithPreviouslyPublishedSlots(post16NovemberWindow)))),
            // WithPupilCampusDataShare(WithSummaryDataShare(WithValueAddedSlots(WithPreviouslyPublishedSlots(post16FebruaryWindow)))),
            // WithAimsSlot(WithPupilCampusDataShare(WithSummaryDataShare(WithValueAddedSlots(WithPreviouslyPublishedSlots(post16MarchWindow)))))
        );
        
        await dbContext.SaveChangesAsync();
    }
}
