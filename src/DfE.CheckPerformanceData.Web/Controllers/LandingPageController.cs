using DfE.CheckPerformanceData.Application.LandingPage;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;
using DeadlineTime = DfE.CheckPerformanceData.Application.WindowManagement.DeadlineTime;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// AB#298317: this controller now asks ICheckingExerciseService about each window's exercises —
// the card and the closed banner are about pupil data checking and results enquiry separately,
// and the outer window dates cannot tell them apart. AB#307773: every answer is for the signed-in
// school, whose extension may run past the exercise's own end.
public sealed class LandingPageController(
    ILogger<LandingPageController> logger,
    ILandingPageService landingPageService,
    ICheckingExerciseService checkingExercises) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        HttpContext.Session.ClearSelectedWindow();
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

        var laestab = result.OrganisationLaestab;
        var landingPageViewModel = new LandingPageViewModel(
            result.OpenWindows.Select(w => Card(w, laestab)).ToList(),
            result.OrganisationName,
            result.OrganisationUrn,
            result.OrganisationLaestab,
            string.Join(',', result.KeyStages.Select(ks => ks.Title)),
            result.OrganisationAddress,
            result.NoDataWindows.Select(Banner).ToList(),
            result.NotValidWindows.Select(Banner).ToList(),
            result.OpenWindows
                .Where(w => HasClosedPupilData(w, laestab))
                .Select(w => ClosedBanner(w, laestab))
                .ToList());

        return View(landingPageViewModel);
    }

    // The banner names a learner, so it carries the window's own noun — "student" on 16-19.
    private static LandingPageBannerViewModel Banner(CheckingWindowDto window) =>
        new() { Title = window.Title, LearnerNoun = LearnerNoun.For(window.CheckingWindowType) };

    private LandingPageWindowViewModel Card(CheckingWindowDto window, string laestab)
    {
        // An extension has no start of its own, so only the end is the school's.
        var pupilDataStart = checkingExercises.StartDateFor(window.Exercises, CheckingExerciseType.PupilData);
        var pupilDataEnd = checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.PupilData, laestab);
        var enquiryEnd = checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.ResultsEnquiry, laestab);

        // Checking-window dates are UK wall-clock values, formatted as they stand — the formats are
        // the ones this card has always used.
        return new LandingPageWindowViewModel
        {
            Title = window.Title,
            Id = window.Id,
            HasPupilData = window.HasPupilData,
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType),
            IsPupilDataOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.PupilData, laestab),
            PupilDataEndTime = pupilDataEnd is { } end ? DeadlineTime.Format(end) : null,
            PupilDataEndDate = pupilDataEnd?.ToString("dddd d MMMM yyyy"),
            PupilDataRangeStart = pupilDataStart?.ToString("d MMMM"),
            PupilDataRangeEnd = pupilDataEnd?.ToString("d MMMM yyyy"),
            IsResultsEnquiryOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.ResultsEnquiry, laestab),
            ResultsEnquiryEndDate = enquiryEnd?.ToString("d MMMM yyyy")
        };
    }

    // A window that runs pupil data checking and has shut it. HasClosed (not !IsOpen) so that an
    // exercise which has not started yet — possible, because exercise dates are validated one
    // exercise at a time — is not announced as closed; and a window with no pupil-data exercise
    // has nothing to have closed, so it gets no banner either. A school in an open extension has
    // not reached its end yet, so it gets no banner (AB#307773).
    private bool HasClosedPupilData(CheckingWindowDto window, string laestab) =>
        checkingExercises.HasClosed(window.Exercises, CheckingExerciseType.PupilData, laestab);

    private LandingPageClosedWindowViewModel ClosedBanner(CheckingWindowDto window, string laestab) =>
        new()
        {
            Title = window.Title,
            NextOpportunity = NextOpportunityText.For(window.NextOpportunity),
            IsResultsEnquiryOpen = checkingExercises.IsOpen(window.Exercises, CheckingExerciseType.ResultsEnquiry, laestab),
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType)
        };
}
