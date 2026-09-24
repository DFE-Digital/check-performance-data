using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using ICheckingDataReader = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingDataReader;
using ExerciseCheckingWindowDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingWindowDto;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

// #317: this controller no longer holds a TimeProvider. Every "is it open" question on this page
// goes through ICheckingExerciseService, which owns the only clock in that path.
public sealed class CheckYourPupilDataController(ICheckYourPupilDataService checkYourPupilDataService,
    ICurrentUserService currentUserService, IAnalyticsService analytics,
    INextStepsService nextSteps, ICheckingExerciseService checkingExercises,
    IExerciseTabBuilder tabBuilder, IExerciseDisplayService display) : Controller
{
    private const int PageSize = 10;
    private const int MaxSearchLength = 100;

    [Route("CheckYourPupilData/{windowId}")]
    public async Task<IActionResult> Index(
        Guid windowId,
        int includedPage = 0, int nonIncludedPage = 0, int resultsPage = 0,
        string? includedSearch = null, string? nonIncludedSearch = null, string? resultsSearch = null,
        string? studentDataset = null, string? studentSearch = null, int studentPage = 0, string? tab = null)
    {
        if (includedSearch?.Length > MaxSearchLength) includedSearch = null;
        if (nonIncludedSearch?.Length > MaxSearchLength) nonIncludedSearch = null;
        if (resultsSearch?.Length > MaxSearchLength) resultsSearch = null;
        if (studentSearch?.Length > MaxSearchLength) studentSearch = null;

        HttpContext.Session.ClearRequestState(windowId);
        var model = await BuildIndexModelAsync(windowId, includedPage, nonIncludedPage, resultsPage,
            includedSearch, nonIncludedSearch, resultsSearch, studentDataset, studentSearch, studentPage, tab);
        return View(model);
    }

    [Route("CheckYourPupilData/{windowId}/download/all")]
    public async Task<IActionResult> DownloadAll(Guid windowId)
    {
        // The exercise tabs, rebuilt for this school. A window with schema-backed datasets zips
        // one CSV per dataset instead; every KS2 and KS4 window today draws no exercise tabs, so
        // this falls straight through to the pupil-inclusion zip below, exactly as before.
        var model = await BuildIndexModelAsync(windowId, 0, 0, 0, null, null, null);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        var datasets = model.CheckingExerciseTabs.SelectMany(GetDatasets).ToList();
        if (datasets.Count > 0)
        {
            using var archiveBytes = new MemoryStream();
            await using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var dataset in datasets)
                {
                    var entryName = await GenerateCsvFileName(windowId, CsvPrefix(dataset), window);
                    await using var entry = await archive.CreateEntry(entryName).OpenAsync();
                    await entry.WriteAsync(display.Csv(dataset));
                }
            }
            return File(archiveBytes.ToArray(), "application/zip", GenerateZipFileName(window));
        }

        var included = await checkYourPupilDataService.GetPupilCsvAsync(windowId, included: true);
        var nonIncluded = await checkYourPupilDataService.GetPupilCsvAsync(windowId, included: false);

        var includedCsv = PupilCsvGenerator.Generate(included);
        var nonIncludedCsv = PupilCsvGenerator.Generate(nonIncluded);

        using var ms = new MemoryStream();
        await using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var includedCsvFilename = await GenerateCsvFileName(windowId, "pupil-include", window);
            await using (var s1 = await zip.CreateEntry(includedCsvFilename).OpenAsync())
                s1.Write(includedCsv);

            var nonIncludedCsvFilename = await GenerateCsvFileName(windowId, "pupil-non-include", window);
            await using (var s2 = await zip.CreateEntry(nonIncludedCsvFilename).OpenAsync())
                s2.Write(nonIncludedCsv);

            // The results CSV rides along only on a window that has a Results tab.
            if (await checkYourPupilDataService.GetResultsCsvAsync(windowId) is { } results)
            {
                var resultsCsvFilename = await GenerateCsvFileName(windowId, "results", window);
                await using var s3 = await zip.CreateEntry(resultsCsvFilename).OpenAsync();
                s3.Write(PupilCsvGenerator.Generate(results));
            }
        }

        var zipFileName = GenerateZipFileName(window);
        return File(ms.ToArray(), "application/zip", zipFileName);
    }

    [Route("CheckYourPupilData/{windowId}/download/included")]
    public async Task<IActionResult> DownloadIncluded(Guid windowId)
    {
        var filename = await GenerateCsvFileName(windowId, "pupil-include");
        var pupils = await checkYourPupilDataService.GetPupilCsvAsync(windowId, included: true);
        var bytes = PupilCsvGenerator.Generate(pupils);
        return File(bytes, "text/csv", filename);
    }

    // Rebuilds the tabs for this school and finds the dataset by exercise id and key, so an id
    // from another window (or a dataset this window does not run) reaches nothing. An exercise
    // split by inclusion draws two tabs, so the tab key picks one; with no key, the first.
    [Route("CheckYourPupilData/{windowId}/download/exercise/{exerciseId:guid}/{dataset}")]
    public async Task<IActionResult> DownloadExerciseDataset(Guid windowId, Guid exerciseId, string dataset,
        string? tab = null)
    {
        if (string.IsNullOrWhiteSpace(currentUserService.OrganisationLaestab)) return Forbid();

        var model = await BuildIndexModelAsync(windowId, 0, 0, 0, null, null, null);
        var exerciseTab = model.CheckingExerciseTabs
            .FirstOrDefault(t => t.Exercise.Id == exerciseId && (tab is null || t.Key == tab));
        var selected = exerciseTab?.Table?.Datasets.SingleOrDefault(d => d.Key == dataset)
            ?? (exerciseTab?.Vertical?.Key == dataset ? exerciseTab.Vertical.Dataset : null);

        if (selected is null) return NotFound();

        return File(display.Csv(selected), "text/csv", await GenerateCsvFileName(windowId, CsvPrefix(selected)));
    }

    // The raw JSON the ingress run wrote, unshaped by any schema. This is the endpoint the POC put
    // on /check-data; that page is not ported, so it lives here with the tabs it belongs to.
    [Route("CheckYourPupilData/{windowId}/download/exercise/{exerciseId:guid}/json")]
    public async Task<IActionResult> DownloadExerciseJson(Guid windowId, Guid exerciseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUserService.OrganisationLaestab)) return Forbid();

        var model = await BuildIndexModelAsync(windowId, 0, 0, 0, null, null, null);
        // The raw file is the whole exercise's, so either of an exercise's inclusion tabs serves it.
        var tab = model.CheckingExerciseTabs.FirstOrDefault(t => t.Exercise.Id == exerciseId);
        if (tab is null) return NotFound();

        // Re-read rather than re-serialise the parsed rows: the file a school downloads must be
        // the data the run produced (with a release, each dataset's file under its name).
        var bytes = await tabBuilder.ReadRawAsync(tab, currentUserService.OrganisationLaestab!, cancellationToken);

        return bytes is null ? NotFound() : File(bytes, "application/json", $"{tab.Exercise.TabName}.json");
    }

    // A dataset's file name, less ".csv", is the prefix. GenerateCsvFileName adds the school, the
    // window type and the year, as the pupil CSVs have always been named.
    private static string CsvPrefix(ExerciseDataset dataset) => Path.GetFileNameWithoutExtension(dataset.FileName);

    private static IEnumerable<ExerciseDataset> GetDatasets(ExerciseTab tab) =>
        tab.Table?.Datasets ?? (tab.Vertical is not null ? [tab.Vertical.Dataset] : []);

    private string GenerateZipFileName(CheckingWindowDto window)
    {
        var urn = currentUserService.OrganisationUrn;
        var filename = $"{urn}-{window.CheckingWindowType.ToString()}-{window.EndDate:yyyy}.zip";
        return filename;
    }
    
    private async Task<string> GenerateCsvFileName(Guid windowId, string prefix, CheckingWindowDto? checkingWindow = null)
    {
        var window = checkingWindow ?? await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var urn = currentUserService.OrganisationUrn;
        var filename = $"{prefix}-{urn}-{window.CheckingWindowType.ToString()}-{window.EndDate:yyyy}.csv";
        return filename;
    }

    [Route("CheckYourPupilData/{windowId}/download/non-included")]
    public async Task<IActionResult> DownloadNonIncluded(Guid windowId)
    {
        var filename = await GenerateCsvFileName(windowId, "pupil-non-include");
        var pupils = await checkYourPupilDataService.GetPupilCsvAsync(windowId, included: false);
        var bytes = PupilCsvGenerator.Generate(pupils);
        return File(bytes, "text/csv", filename);
    }

    [Route("CheckYourPupilData/{windowId}/download/results")]
    public async Task<IActionResult> DownloadResults(Guid windowId)
    {
        // The link is never rendered on a window without a Results tab, so a null here is a
        // hand-typed URL. No gated path on this service returns 404.
        var results = await checkYourPupilDataService.GetResultsCsvAsync(windowId);
        if (results is null)
            return RedirectToAction(nameof(Index), new { windowId });

        var filename = await GenerateCsvFileName(windowId, "results");
        return File(PupilCsvGenerator.Generate(results), "text/csv", filename);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("CheckYourPupilData/{windowId}/nextstep")]
    public async Task<IActionResult> NextStep(Guid windowId, CheckYourPupilDataViewModel viewModel)
    {
        // #317: the allowed options are re-derived from the window's open exercises here rather
        // than trusted from the post. Not rendering an option is a UI courtesy; a hand-crafted post
        // must not start a journey for an exercise that is shut, or that this window does not run
        // at all, so it is rejected exactly as an unanswered question would be.
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var allowed = nextSteps.GetAvailableSteps(window.Exercises);

        // AB#298317: "No, I'd like to sign out of this service" is only ever asked when results
        // enquiry is the sole open exercise. Re-derived here, like every other option: a forged
        // SignOut on a page that never asked the question is treated as no answer at all.
        if (viewModel.SelectedNextStep == NextSteps.SignOut && allowed.IsResultsEnquiryOnly())
        {
            // This page requires authentication, so its own Referer is no help once signed out —
            // the default (impersonation-only) Referer-based redirect would bounce straight back
            // into a fresh sign-in challenge instead of showing the school it has signed out.
            return LocalRedirect(SignOutLink.For(HttpContext, Url, returnUrl: "/"));
        }

        if (viewModel.SelectedNextStep is null or NextSteps.SignOut
            || !allowed.Contains(viewModel.SelectedNextStep.Value))
        {
            ModelState.AddModelError(nameof(CheckYourPupilDataViewModel.SelectedNextStep), "Select what you would like to do");
            await analytics.TrackSafeAsync(new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = [ValidationErrorCoding.NoSelection], ErrorFields = [nameof(CheckYourPupilDataViewModel.SelectedNextStep)] });
            var model = await BuildIndexModelAsync(windowId, 0, 0, 0, null, null, null);
            return View("Index", model);
        }

        HttpContext.Session.SaveRequestState(windowId, s => s.SelectedNextStep = viewModel.SelectedNextStep);

        return viewModel.SelectedNextStep switch
        {
            NextSteps.RequestChange => RedirectToAction("Index", "WhatToChange", new { windowId }),
            NextSteps.Confirm => RedirectToAction("Index", "ConfirmCorrect", new { windowId }),
            NextSteps.ResultsEnquiry => RedirectToAction("Index", "ResultIssue", new { windowId }),
            _ => RedirectToAction("Index", "CheckYourPupilData", new { windowId })
        };
    }

    private async Task<CheckYourPupilDataViewModel> BuildIndexModelAsync(
        Guid windowId,
        int includedPage,
        int nonIncludedPage,
        int resultsPage,
        string? includedSearch,
        string? nonIncludedSearch,
        string? resultsSearch,
        string? studentDataset = null,
        string? studentSearch = null,
        int studentPage = 0,
        string? tab = null)
    {
        var (includedTable, includedTotal) = await checkYourPupilDataService.GetPupilTableAsync(windowId, included: true, includedSearch, includedPage, PageSize);
        var (nonIncludedTable, nonIncludedTotal) = await checkYourPupilDataService.GetPupilTableAsync(windowId, included: false, nonIncludedSearch, nonIncludedPage, PageSize);
        // Null when the window has no Results tab (no results enquiry, or no main results slot).
        var results = await checkYourPupilDataService.GetResultsTableAsync(windowId, resultsSearch, resultsPage, PageSize);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        // Fire only on a real search (a term was entered), per section — never the term itself.
        if (!string.IsNullOrEmpty(includedSearch))
            await analytics.TrackSafeAsync(new PupilDataSearchResultsEvent { ResultCount = includedTotal, ActiveTab = "included" });
        if (!string.IsNullOrEmpty(nonIncludedSearch))
            await analytics.TrackSafeAsync(new PupilDataSearchResultsEvent { ResultCount = nonIncludedTotal, ActiveTab = "nonIncluded" });
        if (!string.IsNullOrEmpty(resultsSearch) && results is { } searched)
            await analytics.TrackSafeAsync(new PupilDataSearchResultsEvent { ResultCount = searched.TotalCount, ActiveTab = "results" });

        var journey = HttpContext.Session.GetRequestState(windowId);

        // 16-19 calls a learner a student; every other key stage calls one a pupil. The word is
        // derived from the window type and woven through this page's wording here, so no view has
        // to look it up.
        var noun = LearnerNoun.For(window.CheckingWindowType);

        // Stamp the nav's selected window here, where the type is already in hand: the main nav
        // labels its window-scoped link with this window's noun, so it needs the type as well as
        // the id. Every render of this page re-stamps both.
        HttpContext.Session.SetSelectedWindow(windowId, window.CheckingWindowType);

        // The tab builder wants WindowManagement's CheckingWindowDto (it carries the exercises'
        // TabName and Datasets, which the landing page's own DTO has no use for and does not
        // build on that path). window.Exercises is already that type, so this is a plain reshape,
        // not a second read.
        var exerciseTabs = await tabBuilder.BuildAsync(
            new ExerciseCheckingWindowDto
            {
                Id = window.Id, Title = window.Title, EndDate = window.EndDate,
                KeyStage = window.KeyStage, CheckingWindowType = window.CheckingWindowType,
                StartDate = window.StartDate, Exercises = window.Exercises
            },
            currentUserService.OrganisationLaestab, tab, studentDataset, studentSearch, studentPage,
            PageSize, HttpContext.RequestAborted);

        List<PupilTableSection> sections =
        [
            new()
            {
                Key = "included",
                TabLabel = $"Included {noun.Plural}",
                Heading = $"{noun.SingularCapitalised} included",
                DownloadAction = nameof(DownloadIncluded),
                DownloadLinkText = $"{noun.Singular} included",
                // The empty-state blocks are seeded once per key, so each window type needs its own
                // key to hold its own noun (WindowScopedContentKey).
                EmptyContentKey = WindowScopedContentKey.For("check-pupil-data-no-included-data-content", window.CheckingWindowType),
                EmptyContentHtml = $"""<p>There's no {noun.Singular} included data for your school to check in this window. If you believe this is incorrect, you can <a href="/contact">send us a message</a> or call us on 0300 131 2768</p>""",
                Table = includedTable,
                Page = includedPage,
                TotalPages = TotalPages(includedTotal),
                Search = includedSearch,
                LearnerNoun = noun
            },
            new()
            {
                Key = "nonIncluded",
                TabLabel = $"Non-included {noun.Plural}",
                Heading = $"{noun.SingularCapitalised} non-included",
                DownloadAction = nameof(DownloadNonIncluded),
                DownloadLinkText = $"{noun.Singular} non-included",
                EmptyContentKey = WindowScopedContentKey.For("check-pupil-data-no-non-included-data-content", window.CheckingWindowType),
                EmptyContentHtml = $"""<p>There's no {noun.Singular} non-included data for your school to check in this window. If you believe this is incorrect, you can <a href="/contact">send us a message</a> or call us on 0300 131 2768</p>""",
                Table = nonIncludedTable,
                Page = nonIncludedPage,
                TotalPages = TotalPages(nonIncludedTotal),
                Search = nonIncludedSearch,
                LearnerNoun = noun
            }
        ];

        // The Results tab is dataset-axis, so it is never one of the inclusion-status sections.
        PupilTableSection? resultsSection = results is { } r
            ? new()
            {
                Key = "results",
                TabLabel = "Results",
                Heading = "Results",
                DownloadAction = nameof(DownloadResults),
                DownloadLinkText = "results",
                EmptyContentKey = WindowScopedContentKey.For("check-pupil-data-no-results-data-content", window.CheckingWindowType),
                EmptyContentHtml = """<p>There are no results for your school to check in this window. If you believe this is incorrect, you can <a href="/contact">send us a message</a> or call us on 0300 131 2768</p>""",
                Table = r.Table,
                Page = resultsPage,
                TotalPages = TotalPages(r.TotalCount),
                Search = resultsSearch,
                LearnerNoun = noun,
                SearchLabel = $"Search for a {noun.Singular} by name or subject"
            }
            : null;

        return new CheckYourPupilDataViewModel
        {
            SelectedNextStep = journey.SelectedNextStep,
            WindowId = windowId.ToString(),
            // Empty means this window's exercises draw no tabs — every KS2 and KS4 window today —
            // and the view falls back to the inclusion tabs it has always drawn.
            CheckingExerciseTabs = exerciseTabs,
            WindowTitle = window.Title,
            Sections = sections,
            ResultsSection = resultsSection,
            // 16-19 stacks both populations in one "Pupils" tab, because there the tab axis is
            // dataset (the other 16-19 import files become sibling tabs later), not inclusion.
            SectionsAsTabs = window.CheckingWindowType != CheckingWindowType.Post16,
            // #317: the options are whatever the open exercises offer, for any number of exercises.
            AvailableNextSteps = nextSteps.GetAvailableSteps(window.Exercises),
            // The deadline sentence is about pupil data specifically, so it takes that exercise's
            // own dates. On a multi-exercise window the outer EndDate is months later.
            PupilDataEndDate = checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.PupilData),
            IsPupilDataOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.PupilData),
            IsResultsEnquiryOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.ResultsEnquiry),
            // AB#298317 review: "closed" is the end date having passed, never !IsOpen — see the
            // view model's HasPupilDataClosed remarks.
            HasPupilDataClosed = checkingExercises.HasClosed(window.Exercises, CheckingExerciseType.PupilData),
            HasResultsEnquiryClosed = checkingExercises.HasClosed(window.Exercises, CheckingExerciseType.ResultsEnquiry),
            NextOpportunity = NextOpportunityText.For(window.NextOpportunity),
            OrganisationName = currentUserService.OrganisationName,
            LearnerNoun = noun,
            TitleContentKey = WindowScopedContentKey.For("check-pupil-data-title", window.CheckingWindowType)
        };
    }

    private static int TotalPages(int count) => (int)Math.Ceiling(count / (double)PageSize);
}

