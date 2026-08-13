using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed class CheckYourPupilDataController(ICheckYourPupilDataService checkYourPupilDataService, TimeProvider timeProvider,
    ICurrentUserService currentUserService, IAnalyticsService analytics) : Controller
{
    private const int PageSize = 10;
    private const int MaxSearchLength = 100;

    [Route("CheckYourPupilData/{windowId}")]
    public async Task<IActionResult> Index(
        Guid windowId,
        int includedPage = 0, int nonIncludedPage = 0,
        string? includedSearch = null, string? nonIncludedSearch = null)
    {
        if (includedSearch?.Length > MaxSearchLength) includedSearch = null;
        if (nonIncludedSearch?.Length > MaxSearchLength) nonIncludedSearch = null;

        HttpContext.Session.SetString("SelectedWindowId", windowId.ToString());
        HttpContext.Session.ClearRequestState(windowId);
        var model = await BuildIndexModelAsync(windowId, includedPage, nonIncludedPage, includedSearch, nonIncludedSearch);
        return View(model);
    }

    [Route("CheckYourPupilData/{windowId}/download/all")]
    public async Task<IActionResult> DownloadAll(Guid windowId)
    {
        var included = await checkYourPupilDataService.GetPupilCsvAsync(windowId, included: true);
        var nonIncluded = await checkYourPupilDataService.GetPupilCsvAsync(windowId, included: false);

        var includedCsv = PupilCsvGenerator.Generate(included);
        var nonIncludedCsv = PupilCsvGenerator.Generate(nonIncluded);

        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        
        using var ms = new MemoryStream();
        await using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var includedCsvFilename = await GenerateCsvFileName(windowId, "pupil-include", window);
            await using (var s1 = await zip.CreateEntry(includedCsvFilename).OpenAsync())
                s1.Write(includedCsv);

            var nonIncludedCsvFilename = await GenerateCsvFileName(windowId, "pupil-non-include", window);
            await using (var s2 = await zip.CreateEntry(nonIncludedCsvFilename).OpenAsync())
                s2.Write(nonIncludedCsv);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("CheckYourPupilData/{windowId}/nextstep")]
    public async Task<IActionResult> NextStep(Guid windowId, CheckYourPupilDataViewModel viewModel)
    {
        if (viewModel.SelectedNextStep is null)
        {
            ModelState.AddModelError(nameof(CheckYourPupilDataViewModel.SelectedNextStep), "Select what you would like to do");
            await analytics.TrackSafeAsync(new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = [ValidationErrorCoding.NoSelection], ErrorFields = [nameof(CheckYourPupilDataViewModel.SelectedNextStep)] });
            var model = await BuildIndexModelAsync(windowId, 0, 0, null, null);
            return View("Index", model);
        }

        HttpContext.Session.SaveRequestState(windowId, s => s.SelectedNextStep = viewModel.SelectedNextStep);

        return viewModel.SelectedNextStep switch
        {
            NextSteps.RequestChange => RedirectToAction("Index", "WhatToChange", new { windowId }),
            NextSteps.Confirm => RedirectToAction("Index", "ConfirmCorrect", new { windowId }),
            _ => RedirectToAction("Index", "CheckYourPupilData", new { windowId })
        };
    }

    private async Task<CheckYourPupilDataViewModel> BuildIndexModelAsync(
        Guid windowId,
        int includedPage,
        int nonIncludedPage,
        string? includedSearch,
        string? nonIncludedSearch)
    {
        var (includedTable, includedTotal) = await checkYourPupilDataService.GetPupilTableAsync(windowId, included: true, includedSearch, includedPage, PageSize);
        var (nonIncludedTable, nonIncludedTotal) = await checkYourPupilDataService.GetPupilTableAsync(windowId, included: false, nonIncludedSearch, nonIncludedPage, PageSize);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        // Fire only on a real search (a term was entered), per section — never the term itself.
        if (!string.IsNullOrEmpty(includedSearch))
            await analytics.TrackSafeAsync(new PupilDataSearchResultsEvent { ResultCount = includedTotal, ActiveTab = "included" });
        if (!string.IsNullOrEmpty(nonIncludedSearch))
            await analytics.TrackSafeAsync(new PupilDataSearchResultsEvent { ResultCount = nonIncludedTotal, ActiveTab = "nonIncluded" });

        var now = timeProvider.GetLocalNow().DateTime;
        var journey = HttpContext.Session.GetRequestState(windowId);

        List<PupilTableSection> sections =
        [
            new()
            {
                Key = "included",
                TabLabel = "Included pupils",
                Heading = "Pupil included",
                DownloadAction = nameof(DownloadIncluded),
                DownloadLinkText = "pupil included",
                EmptyContentKey = "check-pupil-data-no-included-data-content",
                EmptyContentHtml = """<p>There's no pupil included data for your school to check in this window. If you believe this is incorrect, you can <a href="/contact">send us a message</a> or call us on 0300 131 2768</p>""",
                Table = includedTable,
                Page = includedPage,
                TotalPages = TotalPages(includedTotal),
                Search = includedSearch
            },
            new()
            {
                Key = "nonIncluded",
                TabLabel = "Non-included pupils",
                Heading = "Pupil non-included",
                DownloadAction = nameof(DownloadNonIncluded),
                DownloadLinkText = "pupil non-included",
                EmptyContentKey = "check-pupil-data-no-non-included-data-content",
                EmptyContentHtml = """<p>There's no pupil non-included data for your school to check in this window. If you believe this is incorrect, you can <a href="/contact">send us a message</a> or call us on 0300 131 2768</p>""",
                Table = nonIncludedTable,
                Page = nonIncludedPage,
                TotalPages = TotalPages(nonIncludedTotal),
                Search = nonIncludedSearch
            }
        ];

        return new CheckYourPupilDataViewModel
        {
            SelectedNextStep = journey.SelectedNextStep,
            WindowId = windowId.ToString(),
            WindowEndDate = window.EndDate.ToString("dddd d MMMM yyyy"),
            WindowEndTime = window.EndDate.ToString("htt").ToLower(),
            WindowTitle = window.Title,
            Sections = sections,
            // 16-19 stacks both populations in one "Pupils" tab, because there the tab axis is
            // dataset (the other 16-19 import files become sibling tabs later), not inclusion.
            SectionsAsTabs = window.CheckingWindowType != CheckingWindowType.Post16,
            IsWindowOpen = window.StartDate <= now && now <= window.EndDate,
            OrganisationName = currentUserService.OrganisationName
        };
    }

    private static int TotalPages(int count) => (int)Math.Ceiling(count / (double)PageSize);
}

