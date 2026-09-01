using DfE.CheckPerformanceData.Application.LandingPage;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
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

            // A synthetic dev-impersonation principal has no DfE Sign-In organisation
            // claim, so it can never produce a meaningful landing page. Send the user
            // through the real OIDC sign-in flow rather than out via sign-out —
            // afterwards they'll have real claims AND the impersonation cookie still
            // overlays the editor role on top.
            if (User.Identity?.AuthenticationType == DevImpersonationConstants.Scheme)
            {
                return Challenge(
                    new AuthenticationProperties { RedirectUri = "/LandingPage" },
                    OpenIdConnectDefaults.AuthenticationScheme);
            }

            // DfeSignOut lives on SecretController, not HomeController — Home/DfeSignOut
            // 404s. Pre-existing bug surfaced once dev impersonation made the null path
            // reachable without a real DfE sign-in.
            return RedirectToAction("DfeSignOut", "Account");
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
            result.NoDataWindows.Select(Banner).ToList(),
            result.NotValidWindows.Select(Banner).ToList());
        
        return View(landingPageViewModel);
    }

    // The banner names a learner, so it carries the window's own noun — "student" on 16-19.
    private static LandingPageBannerViewModel Banner(CheckingWindowDto window) =>
        new() { Title = window.Title, LearnerNoun = LearnerNoun.For(window.CheckingWindowType) };
}