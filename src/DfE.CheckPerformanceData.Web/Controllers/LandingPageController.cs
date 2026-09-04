using DfE.CheckPerformanceData.Application.LandingPage;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// AB#298317: this controller now asks ICheckingExerciseService about each window's exercises —
// the card and the closed banner are about pupil data checking and results enquiry separately,
// and the outer window dates cannot tell them apart.
public sealed class LandingPageController(
    ILogger<LandingPageController> logger,
    ILandingPageService landingPageService,
    ICheckingExerciseService checkingExercises) : Controller
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
            result.OpenWindows.Select(Card).ToList(),
            result.OrganisationName,
            result.OrganisationUrn,
            result.OrganisationLaestab,
            string.Join(',', result.KeyStages.Select(ks => ks.Title)),
            result.OrganisationAddress,
            result.NoDataWindows.Select(Banner).ToList(),
            result.NotValidWindows.Select(Banner).ToList(),
            result.OpenWindows.Where(HasClosedPupilData).Select(ClosedBanner).ToList());

        return View(landingPageViewModel);
    }

    // The banner names a learner, so it carries the window's own noun — "student" on 16-19.
    private static LandingPageBannerViewModel Banner(CheckingWindowDto window) =>
        new() { Title = window.Title, LearnerNoun = LearnerNoun.For(window.CheckingWindowType) };

    private LandingPageWindowViewModel Card(CheckingWindowDto window)
    {
        var pupilDataStart = checkingExercises.StartDateFor(window.Exercises, CheckingExerciseType.PupilData);
        var pupilDataEnd = checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.PupilData);
        var enquiryEnd = checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.ResultsEnquiry);

        // Checking-window dates are UK wall-clock values, formatted as they stand — the formats are
        // the ones this card has always used.
        return new LandingPageWindowViewModel
        {
            Title = window.Title,
            Id = window.Id,
            HasPupilData = window.HasPupilData,
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType),
            IsPupilDataOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.PupilData),
            PupilDataEndTime = pupilDataEnd?.ToString("htt").ToLowerInvariant(),
            PupilDataEndDate = pupilDataEnd?.ToString("dddd d MMMM yyyy"),
            PupilDataRangeStart = pupilDataStart?.ToString("d MMMM"),
            PupilDataRangeEnd = pupilDataEnd?.ToString("d MMMM yyyy"),
            IsResultsEnquiryOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.ResultsEnquiry),
            ResultsEnquiryEndDate = enquiryEnd?.ToString("d MMMM yyyy")
        };
    }

    // A window that runs pupil data checking and has shut it. A window with no pupil-data exercise
    // has nothing to have closed, so it gets no banner — fail-closed elsewhere means "no actions",
    // and here it would mean a banner about an exercise the window never ran.
    private bool HasClosedPupilData(CheckingWindowDto window) =>
        checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.PupilData) is not null
        && !checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.PupilData);

    private LandingPageClosedWindowViewModel ClosedBanner(CheckingWindowDto window) =>
        new()
        {
            Title = window.Title,
            NextOpportunity = NextOpportunityText.For(window.NextOpportunity),
            IsResultsEnquiryOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.ResultsEnquiry),
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType)
        };
}
