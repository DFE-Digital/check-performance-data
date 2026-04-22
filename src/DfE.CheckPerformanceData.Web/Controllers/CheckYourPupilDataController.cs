using DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize]
public class CheckYourPupilDataController(ICheckYourPupilDataService checkYourPupilDataService) : Controller
{
    private const int PageSize = 10;

    [Route("CheckYourPupilData/{windowId}")]
    public async Task<IActionResult> Index(
        Guid windowId,
        int includedPage = 0,
        int nonIncludedPage = 0,
        string? includedSearch = null,
        string? nonIncludedSearch = null)
    {
        var model = await BuildIndexModelAsync(windowId, includedPage, nonIncludedPage, includedSearch, nonIncludedSearch);
        return View(model);
    }

    [HttpPost]
    [Route("CheckYourPupilData/{windowId}/nextstep")]
    public async Task<IActionResult> NextStep(Guid windowId, string? selectedNextStep)
    {
        if (string.IsNullOrEmpty(selectedNextStep))
        {
            ModelState.AddModelError(nameof(CheckYourPupilDataViewModel.SelectedNextStep), "Select what you would like to do");
            var model = await BuildIndexModelAsync(windowId, 0, 0, null, null);
            return View("Index", model);
        }

        return RedirectToAction(nameof(Index), new { windowId });
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
