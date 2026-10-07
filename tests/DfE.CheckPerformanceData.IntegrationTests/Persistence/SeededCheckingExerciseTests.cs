using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.Extensions.Logging.Abstractions;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// The dev seed has to produce a window that runs two activities on two different date ranges, or
// none of #315-#320 can be developed or demoed locally. It also has to obey the rule that holds the
// model together: a window's outer dates are the union of its exercises' dates. The four 16-19
// windows are one step of the results enquiry year each ("16 to 19 Oct", "Nov", "Feb" and "Mar"),
// and each Web seed does the steps before its own; the KS4 window is a fixture, ingested by the seed.
[Collection(nameof(AzuriteCollection))]
public sealed class SeededCheckingExerciseTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly Guid _openKs4 = Guid.NewGuid();
    private readonly Guid _october = Guid.NewGuid();
    private readonly Guid _november = Guid.NewGuid();
    private readonly Guid _february = Guid.NewGuid();
    private readonly Guid _march = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _october, _november, _february, _march);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task<CheckingWindow> LoadAsync(Guid windowId)
    {
        await using var ctx = CreateContext();
        return await ctx.CheckingWindows
            .Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .AsNoTracking()
            .SingleAsync(w => w.Id == windowId);
    }

    [Fact]
    public async Task The_October_window_runs_pupil_data_and_results_enquiry_on_different_ranges()
    {
        var window = await LoadAsync(_october);
        var now = DateTime.Now;

        Assert.Equal("16 to 19 Oct", window.Title);
        Assert.Equal(CheckingWindowType.Post16, window.CheckingWindowType);
        var exercises = window.CheckingExercises.OrderBy(e => e.TabOrder).ToList();
        // In tab order: the summary share (the first tab), then pupil data, results enquiry and
        // the pupil campus share. The two shares have no kind.
        Assert.Equal(
            [null, CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry, null],
            exercises.Select(e => e.ExerciseType));
        Assert.All(exercises, e =>
        {
            Assert.True(e.IsEnabled, $"{e.Name} is not enabled");
            Assert.True(e.StartDate <= now && e.EndDate > now, $"{e.Name} is not open");
        });
        // The results enquiry runs to the end of March, long after pupil data checking shuts.
        Assert.Equal((3, 31), (exercises[2].EndDate.Month, exercises[2].EndDate.Day));
        Assert.True(exercises[2].EndDate > exercises[1].EndDate.AddMonths(3));
        Assert.Equal(new DateTime(now.Year + 1, 10, 1), window.NextOpportunity);
    }

    [Fact]
    public async Task The_later_16_to_19_windows_have_pupil_data_checking_shut_but_still_on_view()
    {
        var now = DateTime.Now;
        foreach (var windowId in new[] { _november, _february, _march })
        {
            var window = await LoadAsync(windowId);
            var exercises = window.CheckingExercises.OrderBy(e => e.TabOrder).ToList();

            // Pupil data checking has shut, but the exercise is still live, so the Students tab
            // still shows its data while no school can act on it.
            var pupilData = exercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            Assert.True(pupilData.EndDate < now, $"{window.Title}: pupil data checking is not shut");
            Assert.True(pupilData.IsEnabled);
            Assert.False(pupilData.DisplayOnly);
            Assert.Null(pupilData.VisibleUntil);

            // Everything else is still open, and the outer dates are the union of the exercises.
            Assert.All(exercises.Where(e => e != pupilData), e =>
                Assert.True(e.StartDate <= now && e.EndDate > now, $"{window.Title}: {e.Name} is not open"));
            Assert.Equal(exercises.Min(e => e.StartDate), window.StartDate);
            Assert.Equal(exercises.Max(e => e.EndDate), window.EndDate);
        }
    }

    [Fact]
    public async Task Every_16_to_19_window_has_both_previously_published_slots_and_the_revised_one_waits()
    {
        // The revised slot is empty and not required until the February step fills it.
        foreach (var windowId in new[] { _october, _november, _february, _march })
        {
            var students = (await LoadAsync(windowId)).CheckingExercises
                .Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            // Only the March window has the aims slot.
            string[] aims = windowId == _march ? [SeedCheckingWindows.AimsDataset] : [];
            Assert.Equal(["included", "nonincluded", "previously-published", "previously-published-revised", .. SeedCheckingWindows.ValueAddedDatasets, .. aims],
                students.Datasets.OrderBy(d => d.SortOrder).Select(d => d.Name));
            var revised = students.Datasets.Single(d => d.Name == "previously-published-revised");
            Assert.False(revised.Required);
            Assert.False(revised.FeedsJourney);
            Assert.Equal(string.Empty, revised.IngressFile);
        }
    }

    [Fact]
    public async Task Every_16_to_19_window_has_the_summary_share_and_only_its_first_slot_is_required()
    {
        foreach (var windowId in new[] { _october, _november, _february, _march })
        {
            var summary = Summary(await LoadAsync(windowId));
            Assert.True(summary.DisplayOnly);
            Assert.True(summary.IsEnabled);
            Assert.Equal("Summary", summary.TabName);
            // The Summary tab is first.
            Assert.Equal(summary.Id, (await LoadAsync(windowId)).CheckingExercises.MinBy(e => e.TabOrder)!.Id);
            Assert.Equal(DfE.CheckPerformanceData.Application.CheckYourPupilData.ExerciseLayout.Vertical, summary.Layout);
            var slots = summary.Datasets.OrderBy(d => d.SortOrder).ToList();
            Assert.Equal(SeedCheckingWindows.SummaryDatasets, slots.Select(d => d.Name));
            Assert.Equal([true, false, false, false], slots.Select(d => d.Required));
            Assert.All(slots, d => Assert.False(d.FeedsJourney));
        }
    }

    [Fact]
    public async Task Every_16_to_19_window_has_the_pupil_campus_share()
    {
        foreach (var windowId in new[] { _october, _november, _february, _march })
        {
            var campus = PupilCampus(await LoadAsync(windowId));
            Assert.True(campus.DisplayOnly);
            Assert.True(campus.IsEnabled);
            Assert.Equal("Campus", campus.TabName);
            var slot = Assert.Single(campus.Datasets);
            Assert.Equal(SeedCheckingWindows.PupilCampusDataset, slot.Name);
            Assert.False(slot.FeedsJourney);
        }
    }

    [Fact]
    public async Task Every_16_to_19_window_has_the_value_added_slots_and_they_wait_empty()
    {
        // No value added slot is required until its step fills it: the October run has none.
        foreach (var windowId in new[] { _october, _november, _february, _march })
        {
            var slots = (await LoadAsync(windowId)).CheckingExercises
                .Single(e => e.ExerciseType == CheckingExerciseType.PupilData)
                .Datasets.Where(d => SeedCheckingWindows.ValueAddedDatasets.Contains(d.Name)).ToList();
            Assert.Equal(3, slots.Count);
            Assert.All(slots, d =>
            {
                Assert.False(d.Required);
                Assert.False(d.FeedsJourney);
                Assert.Equal(string.Empty, d.IngressFile);
            });
        }
    }

    [Fact]
    public async Task The_window_seed_gives_every_slot_but_no_data()
    {
        // The Web seed imports the files: the window seed alone links nothing and runs nothing.
        var window = await LoadAsync(_october);

        var students = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
        Assert.Equal(["included", "nonincluded", "previously-published", "previously-published-revised", .. SeedCheckingWindows.ValueAddedDatasets],
            students.Datasets.OrderBy(d => d.SortOrder).Select(d => d.Name));
        // The previously published and value added data are shown to schools, never read by a journey.
        Assert.Equal([true, true, false, false, false, false, false], students.Datasets.OrderBy(d => d.SortOrder).Select(d => d.FeedsJourney));

        var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        Assert.Equal(
            DfE.CheckPerformanceData.Application.ResultsEnquiry.ResultsSources.For(CheckingWindowType.Post16).Select(s => s.Tag),
            results.Datasets.OrderBy(d => d.SortOrder).Select(d => d.Name));

        Assert.All(window.CheckingExercises, e =>
        {
            Assert.Null(e.CurrentReleaseId);
            Assert.Null(e.Validated);
            Assert.All(e.Datasets, d => Assert.True(d.IngressFile == string.Empty && d.SchemaFile == string.Empty, d.Name));
        });
    }

    [Fact]
    public async Task A_single_activity_window_gets_one_pupil_data_exercise_on_its_own_dates()
    {
        var window = await LoadAsync(_openKs4);

        var exercise = Assert.Single(window.CheckingExercises);
        Assert.Equal(CheckingExerciseType.PupilData, exercise.ExerciseType);
        Assert.Equal(window.StartDate, exercise.StartDate);
        Assert.Equal(window.EndDate, exercise.EndDate);
    }

    // The window's only exercise ends with the window, so VisibleUntil is what keeps the KS4
    // window on the landing page, read only, after pupil data checking closes.
    [Fact]
    public async Task The_KS4_June_exercise_stays_visible_for_a_month_after_it_closes()
    {
        var exercise = Assert.Single((await LoadAsync(_openKs4)).CheckingExercises);

        Assert.Equal(exercise.EndDate.AddMonths(1), exercise.VisibleUntil);
    }

    [Fact]
    public async Task Every_seeded_windows_outer_dates_equal_the_union_of_its_exercises()
    {
        foreach (var windowId in new[] { _openKs4, _october, _november, _february, _march })
        {
            var window = await LoadAsync(windowId);

            Assert.NotEmpty(window.CheckingExercises);
            Assert.Equal(window.StartDate, window.CheckingExercises.Min(e => e.StartDate));
            Assert.Equal(window.EndDate, window.CheckingExercises.Max(e => e.EndDate));
        }
    }

    [Fact]
    public async Task Window_seed_can_be_repeated()
    {
        // The dev seed deletes every window on each start.
        for (var start = 0; start < 2; start++)
        {
            await using var ctx = CreateContext();
            await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _october, _november, _february, _march);
        }

        Assert.Equal(4, (await LoadAsync(_october)).CheckingExercises.Count);
    }

    [Fact]
    public async Task The_October_window_has_the_October_files_validated()
    {
        // The seed does what the admin does by hand: choose each sample CSV and schema for its slot,
        // then validate. The journey then finds the students and every October result, each with
        // the tag of the file it came in, and the second late results file is still awaited.
        await SeedAsync(_october, SeedPost16OctoberSamples.ExecuteSeedAsync);
        try
        {
            var october = await LoadAsync(_october);
            Assert.All(october.CheckingExercises, e =>
            {
                Assert.NotNull(e.CurrentReleaseId);
                Assert.NotNull(e.Validated);
            });

            var studentsExercise = october.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            var pupils = DfE.CheckPerformanceData.Infrastructure.BlobStorage.PupilDataBlobClient.Deserialize(
                await ReadAsync(_october, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(studentsExercise.Id, CheckingDataType.Pupil, "860/4070", studentsExercise.CurrentReleaseId)),
                CheckingWindowType.Post16);
            // The journeys' pupils file holds the two student files only, not the previously published one.
            Assert.Equal(240, pupils.Count);
            Assert.Equal(120, pupils.Count(p => p.IsIncluded));

            // The previously published data has its own file per school, split by LAESTAB_0.
            var previous = studentsExercise.Datasets.Single(d => d.Name == "previously-published");
            Assert.NotEqual(string.Empty, previous.IngressFile);
            var published = Newtonsoft.Json.Linq.JArray.Parse(System.Text.Encoding.UTF8.GetString(
                await ReadAsync(_october, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DatasetBlobName(studentsExercise.Id, studentsExercise.CurrentReleaseId!.Value, previous.Id, "860/4070"))));
            Assert.Equal(60, published.Count);
            Assert.All(published, r => Assert.Equal("8604070", r["LAESTAB_0"]!.ToString()));

            var results = Results(october);
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_NONINC"], Linked(results));
            Assert.Equal(1, await ReleaseCountAsync(results));
            var seeded = await ResultsAsync(_october, results);
            Assert.Equal(
                SeedStudentResults.All.Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_NONINC"], seeded.Select(r => r.SourceFile).Distinct().Order());
            Assert.True(await AwaitingSecondLateResultsAsync(_october));

            await AssertPupilCampusAsync(_october, october);
            await AssertSummaryAsync(_october, october, step: 0);
            await AssertValueAddedAsync(_october, october, step: -1);
        }
        finally
        {
            await DeleteBlobsAsync(_october);
        }
    }

    [Fact]
    public async Task The_November_window_has_late_results_2_validated_after_the_October_files()
    {
        // The October import, then late results 2 and a second run: a new release that holds it.
        await SeedAsync(_november, SeedPost16NovemberSamples.ExecuteSeedAsync);
        try
        {
            var november = await LoadAsync(_november);
            Assert.Equal("16 to 19 Nov", november.Title);
            var results = Results(november);
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_LR2", "16to19_NONINC"], Linked(results));
            Assert.DoesNotContain(results.Datasets, d => d.Retired);
            Assert.Equal(2, await ReleaseCountAsync(results));

            var seeded = await ResultsAsync(_november, results);
            Assert.Equal(
                SeedStudentResults.All.Concat(SeedStudentResults.LateResults2).Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            // A student who held nothing in October now has a result.
            Assert.Contains(seeded, r => r.CypmdId == "500005" && r.SourceFile == "16to19_LR2");
            Assert.False(await AwaitingSecondLateResultsAsync(_november));

            await AssertPupilCampusAsync(_november, november);
            await AssertSummaryAsync(_november, november, step: 1);
            await AssertValueAddedAsync(_november, november, step: 0);
        }
        finally
        {
            await DeleteBlobsAsync(_november);
        }
    }

    [Fact]
    public async Task The_February_window_has_the_revised_files_validated_and_the_four_they_replace_retired()
    {
        // The November steps, then the revised files: the release holds only the revised rows.
        await SeedAsync(_february, SeedPost16FebruarySamples.ExecuteSeedAsync);
        try
        {
            var february = await LoadAsync(_february);
            Assert.Equal("16 to 19 Feb", february.Title);
            var results = Results(february);
            Assert.Equal(
                ["16to19_INC", "16to19_INC_REV", "16to19_LR1", "16to19_LR2", "16to19_NONINC", "16to19_NONINC_REV"],
                Linked(results));
            Assert.Equal(["16to19_INC", "16to19_LR1", "16to19_LR2", "16to19_NONINC"],
                results.Datasets.Where(d => d.Retired).Select(d => d.Name).Order());
            Assert.Equal(3, await ReleaseCountAsync(results));

            var revised = await ResultsAsync(_february, results);
            Assert.Equal(SeedStudentResults.Revised.Select(r => r.CompositeKey).Order(), revised.Select(r => r.CompositeKey).Order());
            // The amendment replaced the row it corrected: one English row for Alice, at grade 7.
            Assert.Equal("7", Assert.Single(revised, r => r.CypmdId == "500001" && r.Qan == "60148366").Grade);
            Assert.False(await AwaitingSecondLateResultsAsync(_february));

            // The February step fills the previously published revised slot, makes it required and
            // retires previously published. With the value added runs (November, then February's
            // revised file), that is the fourth pupil data release.
            var students = february.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            Assert.Equal(["included", "nonincluded", "previously-published", "previously-published-revised", .. SeedCheckingWindows.ValueAddedDatasets],
                students.Datasets.OrderBy(d => d.SortOrder).Select(d => d.Name));
            Assert.Equal(["previously-published", SeedCheckingWindows.ValueAddedDatasets[0]], students.Datasets.Where(d => d.Retired).OrderBy(d => d.SortOrder).Select(d => d.Name));
            var previous = students.Datasets.Single(d => d.Name == "previously-published-revised");
            Assert.False(previous.FeedsJourney);
            Assert.True(previous.Required);
            Assert.NotEqual(string.Empty, previous.IngressFile);
            Assert.Equal(4, await ReleaseCountAsync(students));
            var published = Newtonsoft.Json.Linq.JArray.Parse(System.Text.Encoding.UTF8.GetString(
                await ReadAsync(_february, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DatasetBlobName(students.Id, students.CurrentReleaseId!.Value, previous.Id, "860/4070"))));
            // October's 60, less 6, plus 10.
            Assert.Equal(64, published.Count);

            await AssertPupilCampusAsync(_february, february);
            await AssertSummaryAsync(_february, february, step: 2);
            await AssertValueAddedAsync(_february, february, step: 1);
        }
        finally
        {
            await DeleteBlobsAsync(_february);
        }
    }

    [Fact]
    public async Task The_March_window_has_included_revised_with_retention_validated_and_included_revised_retired()
    {
        // The February steps, then included revised with retention replaces included revised. The
        // non-included revised file stays in use.
        await SeedAsync(_march, SeedPost16MarchSamples.ExecuteSeedAsync);
        try
        {
            var march = await LoadAsync(_march);
            Assert.Equal("16 to 19 Mar", march.Title);
            var results = Results(march);
            Assert.Equal(["16to19_INC", "16to19_INC_REV", "16to19_LR1", "16to19_LR2", "16to19_NONINC"],
                results.Datasets.Where(d => d.Retired).Select(d => d.Name).Order());
            Assert.Equal(["16to19_INC_REV_RET", "16to19_NONINC_REV"],
                results.Datasets.Where(d => !d.Retired && d.IngressFile != string.Empty).Select(d => d.Name).Order());
            Assert.Equal(4, await ReleaseCountAsync(results));

            var seeded = await ResultsAsync(_march, results);
            Assert.Equal(
                SeedStudentResults.IncludedRevisedWithRetention
                    .Concat(SeedStudentResults.Revised.Where(r => r.SourceFile == "16to19_NONINC_REV"))
                    .Select(r => r.CompositeKey).Order(),
                seeded.Select(r => r.CompositeKey).Order());
            Assert.Equal(["16to19_INC_REV_RET", "16to19_NONINC_REV"], seeded.Select(r => r.SourceFile).Distinct().Order());
            // The grade only the retention file changes: Charlie Smith's Maths, 3 → 4.
            Assert.Equal("4", Assert.Single(seeded, r => r.CypmdId == "500003" && r.Qan == "60146084" && r.Session == "S2024").Grade);

            // Pupil aims is a slot in pupil data, not an exercise of its own: display only, filled
            // and required, and in pupil data's March release with the value added file.
            Assert.DoesNotContain(march.CheckingExercises, e => e.Name == "Pupil aims");
            var marchStudents = march.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
            var aims = Assert.Single(marchStudents.Datasets, d => d.Name == SeedCheckingWindows.AimsDataset);
            Assert.False(aims.FeedsJourney);
            Assert.True(aims.Required);
            Assert.NotEqual(string.Empty, aims.IngressFile);
            Assert.NotEmpty(await ReadAsync(_march, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                .DatasetBlobName(marchStudents.Id, marchStudents.CurrentReleaseId!.Value, aims.Id, "860/4070")));

            await AssertPupilCampusAsync(_march, march);
            await AssertSummaryAsync(_march, march, step: 3);
            await AssertValueAddedAsync(_march, march, step: 2);
        }
        finally
        {
            await DeleteBlobsAsync(_march);
        }
    }

    private async Task SeedAsync(Guid windowId,
        Func<IPortalDbContext, BlobServiceClient, ICheckingExerciseIngress, string, Guid, Task> seed)
    {
        await using var ctx = CreateContext();
        await seed(ctx, new BlobServiceClient(azurite.ConnectionString), Ingress(ctx), AppContext.BaseDirectory, windowId);
    }

    private static CheckingExercise PupilCampus(CheckingWindow window) =>
        window.CheckingExercises.Single(e => e.ExerciseType is null && e.Name == SeedCheckingWindows.PupilCampusExercise);

    // The October step fills the campus slot and validates; the later steps keep that release.
    // The live file holds a row for each of the school's included students.
    private async Task AssertPupilCampusAsync(Guid windowId, CheckingWindow window)
    {
        var campus = PupilCampus(window);
        var slot = Assert.Single(campus.Datasets);
        Assert.NotEqual(string.Empty, slot.IngressFile);
        Assert.Equal(1, await ReleaseCountAsync(campus));

        var school = Newtonsoft.Json.Linq.JArray.Parse(System.Text.Encoding.UTF8.GetString(
            await ReadAsync(windowId, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                .DatasetBlobName(campus.Id, campus.CurrentReleaseId!.Value, slot.Id, "860/4070"))));
        Assert.NotEmpty(school);
        Assert.All(school, r => Assert.Equal("8604070", r["LAESTAB"]!.ToString()));
        Assert.Contains(school, r => r["CampID"]!.ToString() == SeedPost16PupilCampus.SecondCampus("8604070"));
    }

    private static CheckingExercise Summary(CheckingWindow window) =>
        window.CheckingExercises.Single(e => e.ExerciseType is null && e.Name == SeedCheckingWindows.SummaryExercise);

    // Each step fills its summary slot, makes it required and retires the slot before it, and each
    // step's run is a release. The live file holds one row for the school.
    private async Task AssertSummaryAsync(Guid windowId, CheckingWindow window, int step)
    {
        var summary = Summary(window);
        var slots = summary.Datasets.OrderBy(d => d.SortOrder).ToList();
        Assert.Equal(SeedCheckingWindows.SummaryDatasets.Take(step + 1), Linked(summary).Order());
        Assert.Equal(SeedCheckingWindows.SummaryDatasets.Take(step), slots.Where(d => d.Retired).Select(d => d.Name));
        Assert.True(slots[step].Required);
        Assert.Equal(step + 1, await ReleaseCountAsync(summary));

        var school = Newtonsoft.Json.Linq.JArray.Parse(System.Text.Encoding.UTF8.GetString(
            await ReadAsync(windowId, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                .DatasetBlobName(summary.Id, summary.CurrentReleaseId!.Value, slots[step].Id, "860/4070"))));
        Assert.Equal("8604070", Assert.Single(school)["LAESTAB"]!.ToString());
    }

    // Each step from November fills its value added slot in pupil data, makes it required and
    // retires the slot before it. The live file is split by Laestab and feeds no journey. October
    // (step -1) has none.
    private async Task AssertValueAddedAsync(Guid windowId, CheckingWindow window, int step)
    {
        var students = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
        var slots = students.Datasets.Where(d => SeedCheckingWindows.ValueAddedDatasets.Contains(d.Name))
            .OrderBy(d => d.SortOrder).ToList();
        Assert.Equal(SeedCheckingWindows.ValueAddedDatasets.Take(step + 1),
            slots.Where(d => d.IngressFile != string.Empty).Select(d => d.Name));
        Assert.Equal(SeedCheckingWindows.ValueAddedDatasets.Take(Math.Max(step, 0)),
            slots.Where(d => d.Retired).Select(d => d.Name));
        if (step < 0)
            return;

        Assert.True(slots[step].Required);
        var rows = Newtonsoft.Json.Linq.JArray.Parse(System.Text.Encoding.UTF8.GetString(
            await ReadAsync(windowId, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                .DatasetBlobName(students.Id, students.CurrentReleaseId!.Value, slots[step].Id, "860/4070"))));
        // 120 included students, every third with two qualifications.
        Assert.Equal(160, rows.Count);
        Assert.All(rows, r => Assert.Equal("8604070", r["Laestab"]!.ToString()));
    }

    private static CheckingExercise Results(CheckingWindow window) =>
        window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);

    private static IEnumerable<string> Linked(CheckingExercise exercise) =>
        exercise.Datasets.Where(d => d.IngressFile != string.Empty).Select(d => d.Name).Order();

    private async Task<int> ReleaseCountAsync(CheckingExercise exercise)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<CheckingExerciseRelease>().CountAsync(r => r.CheckingExerciseId == exercise.Id);
    }

    private async Task<byte[]> ReadAsync(Guid windowId, string blobName) =>
        (await new BlobServiceClient(azurite.ConnectionString).GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(blobName).DownloadContentAsync()).Value.Content.ToArray();

    private async Task<List<DfE.CheckPerformanceData.Application.ResultsEnquiry.StudentResultRecord>> ResultsAsync(
        Guid windowId, CheckingExercise exercise) =>
        System.Text.Json.JsonSerializer.Deserialize<List<DfE.CheckPerformanceData.Application.ResultsEnquiry.StudentResultRecord>>(
            await ReadAsync(windowId, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                .DataBlobName(exercise.Id, CheckingDataType.Results, "860/4070", exercise.CurrentReleaseId)),
            DfE.CheckPerformanceData.Infrastructure.BlobStorage.StudentResultsBlobClient.JsonOptions)!;

    private async Task<bool> AwaitingSecondLateResultsAsync(Guid windowId)
    {
        await using var ctx = CreateContext();
        return await new DfE.CheckPerformanceData.Application.ResultsEnquiry.LateResultsWarning(
            new DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseStorageResolver(
                new WindowRepository(ctx), TimeProvider.System)).ShowAsync(windowId);
    }

    private Task DeleteBlobsAsync(Guid windowId) =>
        new BlobServiceClient(azurite.ConnectionString).GetBlobContainerClient(windowId.ToString()).DeleteIfExistsAsync();

    private CheckingExerciseIngress Ingress(PortalDbContext ctx) =>
        new(new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
            new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                new Dictionary<string, BlobServiceClient> { ["app"] = new BlobServiceClient(azurite.ConnectionString) }),
            TimeProvider.System);

    [Fact]
    public async Task Fixture_windows_are_ingested_so_every_exercise_has_a_release_and_journey_data()
    {
        // The KS4 fixture windows are seeded through ingress, like an admin upload, so the Check
        // Your Pupil Data page draws schema-driven tabs for them. The journeys still read the
        // same fixtures: pupils by Id.
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        try
        {
            await using (var ctx = CreateContext())
            {
                var ingress = new CheckingExerciseIngress(
                    new CheckingExerciseDefinitionRepository(ctx, new WindowRepository(ctx)),
                    new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                        new Dictionary<string, BlobServiceClient> { ["app"] = blobs }), TimeProvider.System);
                await SeedExerciseFixtures.ExecuteSeedAsync(ctx, blobs, ingress, AppContext.BaseDirectory,
                    [_openKs4]);
            }

            Assert.All((await LoadAsync(_openKs4)).CheckingExercises, e =>
            {
                Assert.True(e.IsEnabled, $"{e.Name} is not enabled");
                Assert.False(string.IsNullOrWhiteSpace(e.TabName), $"{e.Name} has no tab");
                Assert.NotNull(e.CurrentReleaseId);
                Assert.NotNull(e.Validated);
                Assert.All(e.Datasets, d => Assert.False(string.IsNullOrEmpty(d.SchemaFile)));
            });

            // KS4: Alice Smith, whom the E2E suite drives by UPN, keeps a real Id.
            var ks4 = (await LoadAsync(_openKs4)).CheckingExercises.Single();
            var ks4Pupils = DfE.CheckPerformanceData.Infrastructure.BlobStorage.PupilDataBlobClient.Deserialize(
                await ReadAsync(_openKs4, DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseBlobPaths
                    .DataBlobName(ks4.Id, CheckingDataType.Pupil, "860/4070", ks4.CurrentReleaseId)),
                CheckingWindowType.KS4June);
            var alice = Assert.Single(ks4Pupils, p => p.Identifier == "A86040700001B");
            Assert.Equal(("Alice", "Smith"), (alice.Firstname, alice.Surname));
            Assert.NotEqual(Guid.Empty, alice.Id);
            Assert.Equal(240, ks4Pupils.Count);
        }
        finally
        {
            await blobs.GetBlobContainerClient(_openKs4.ToString()).DeleteIfExistsAsync();
        }

        async Task<byte[]> ReadAsync(Guid windowId, string blobName) =>
            (await blobs.GetBlobContainerClient(windowId.ToString()).GetBlobClient(blobName).DownloadContentAsync())
                .Value.Content.ToArray();
    }
}
