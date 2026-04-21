using DfE.CheckPerformanceData.Application.Features.LandingPage;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class LandingController(ILogger<LandingController> logger, ILandingPageService landingPageService) : Controller
{
    [Authorize]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await landingPageService.GetLandingPageDataAsync(cancellationToken);

        if (result == null)
        {
            logger.LogWarning("No landing page data found for the current user");
            return RedirectToAction("DfeSignOut", "Home");
        }
        
        var landingPageViewModel = new LandingPageViewModel(
            result.OpenWindows.Select(w => new LandingPageWindowViewModel
                { Title = w.Title, EndDate = w.EndDate, Id = w.Id }),
            result.OrganisationName,
            result.OrganisationUrn,
            result.OrganisationLaestab, string.Join(',', result.KeyStages.Select(ks => ks.Title)));
        
        return View(landingPageViewModel);
    }
}