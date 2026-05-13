using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class LandingPageController(ILogger<LandingPageController> logger, ILandingPageService landingPageService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        HttpContext.Session.Remove("SelectedWindowId");
        var result = await landingPageService.GetLandingPageDataAsync(cancellationToken);

        if (result == null)
        {
            logger.LogWarning("No landing page data found for the current user");
            // DfeSignOut lives on SecretController, not HomeController — Home/DfeSignOut
            // 404s. Pre-existing bug surfaced once dev impersonation made the null path
            // reachable without a real DfE sign-in.
            return RedirectToAction("DfeSignOut", "Secret");
        }

        var landingPageViewModel = new LandingPageViewModel(
            result.OpenWindows.Select(w => new LandingPageWindowViewModel
            {
                Title = w.Title,
                EndDate = w.EndDate.ToString("dddd d MMMM yyyy"),
                EndTime = w.EndDate.ToString("htt").ToLower(),
                StartDate = w.StartDate.ToString("dddd d MMMM yyyy"),
                Id = w.Id,
                HasPupilData = w.HasPupilData
            }),
            result.OrganisationName,
            result.OrganisationUrn,
            result.OrganisationLaestab,
            string.Join(',', result.KeyStages.Select(ks => ks.Title)),
            result.OrganisationAddress,
            result.NoDataWindowsText,
            result.NotValidWindowsText);
        
        return View(landingPageViewModel);
    }
}