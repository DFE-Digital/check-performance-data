using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed class CheckYourPupilDataController(ICheckYourPupilDataService checkYourPupilDataService, TimeProvider timeProvider, 
    ICurrentUserService currentUserService) : Controller
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
        var included = await checkYourPupilDataService.GetIncludedPupilsCsvAsync(windowId);
        var nonIncluded = await checkYourPupilDataService.GetNonIncludedPupilsCsvAsync(windowId);

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
        var pupils = await checkYourPupilDataService.GetIncludedPupilsCsvAsync(windowId);
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
        var pupils = await checkYourPupilDataService.GetNonIncludedPupilsCsvAsync(windowId);
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
        var (included, includedTotal) = await checkYourPupilDataService.GetIncludedPupilsAsync(windowId, includedSearch, includedPage, PageSize);
        var (nonIncluded, nonIncludedTotal) = await checkYourPupilDataService.GetNonIncludedPupilsAsync(windowId, nonIncludedSearch, nonIncludedPage, PageSize);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        var now = timeProvider.GetLocalNow().DateTime;
        var journey = HttpContext.Session.GetRequestState(windowId);

        return new CheckYourPupilDataViewModel
        {
            SelectedNextStep = journey.SelectedNextStep,
            WindowId = windowId.ToString(),
            WindowEndDate = window.EndDate.ToString("dddd d MMMM yyyy"),
            WindowEndTime = window.EndDate.ToString("htt").ToLower(),
            WindowTitle = window.Title,
            IncludedPupils = included.Select(ToPupilRow).ToList(),
            IncludedPupilsPage = includedPage,
            IncludedPupilsTotalPages = TotalPages(includedTotal),
            IncludedSearch = includedSearch,
            NonIncludedPupils = nonIncluded.Select(ToPupilRow).ToList(),
            NonIncludedPupilsPage = nonIncludedPage,
            NonIncludedPupilsTotalPages = TotalPages(nonIncludedTotal),
            NonIncludedSearch = nonIncludedSearch,
            IsWindowOpen = window.StartDate <= now && now <= window.EndDate,
            OrganisationName = currentUserService.OrganisationName
        };
    }

    private static PupilRow ToPupilRow(PupilDto p) => new()
    {
        Surname = p.Surname,
        Firstname = p.Firstname,
        Sex = p.Sex,
        DateOfBirth = p.DateOfBirth,
        Age = p.Age,
        Cypmd_Id = p.Cypmd_Id
    };

    private static int TotalPages(int count) => (int)Math.Ceiling(count / (double)PageSize);
}

