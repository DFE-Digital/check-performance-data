using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public class CheckYourPupilDataController(ICheckYourPupilDataService checkYourPupilDataService) : Controller
{
    private const int PageSize = 10;

    [Route("CheckYourPupilData/{windowId}")]
    public async Task<IActionResult> Index(
        Guid windowId,
        int includedPage = 0, int nonIncludedPage = 0,
        string? includedSearch = null, string? nonIncludedSearch = null)
    {
        var model = await BuildIndexModelAsync(windowId, includedPage, nonIncludedPage, includedSearch, nonIncludedSearch);
        return View(model);
    }

    [HttpPost]
    [Route("CheckYourPupilData/{windowId}/nextstep")]
    public async Task<IActionResult> NextStep(Guid windowId, CheckYourPupilDataViewModel viewModel)
    {
        if (viewModel.SelectedNextStep is null)
        {
            ModelState.AddModelError(nameof(CheckYourPupilDataViewModel.SelectedNextStep), "Select what you would like to do");
            var model = await BuildIndexModelAsync(windowId, 0, 0, null, null);
            return View("Index", model);
        }

        return viewModel.SelectedNextStep switch
        {
            NextSteps.RequestChange => RedirectToAction("Index", "WhatToChange", new { windowId }),
            NextSteps.Confirm => RedirectToAction("Index", "Confirm", new { windowId }),
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

        return new CheckYourPupilDataViewModel
        {
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
            NonIncludedSearch = nonIncludedSearch
        };
    }

    private static PupilRow ToPupilRow(PupilDto p) => new()
    {
        Surname = p.Surname,
        Firstname = p.Firstname,
        Sex = p.Sex,
        DateOfBirth = p.DateOfBirth,
        Age = p.Age,
        FirstLanguage = p.FirstLanguage
    };

    private static int TotalPages(int count) => (int)Math.Ceiling(count / (double)PageSize);
}

