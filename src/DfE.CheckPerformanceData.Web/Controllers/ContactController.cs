using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The Contact Us wayfinder is a placeholder entry point: it validates only the enquiry type, saves
// nothing, and emits a custom analytics event + an activity log line. [AllowAnonymous] because the
// app's global fallback policy requires authentication; anonymous users need this channel too.
[AllowAnonymous]
public sealed class ContactController(
    IAnalyticsService analytics,
    ICurrentUserService currentUser,
    IContentBlockService contentBlocks,
    ILogger<ContactController> logger) : Controller
{
    private const string HighlightBlockKey = "contact-highlight";
    private const string GuidanceLanding = "/guidance";

    [HttpGet("/contact")]
    public async Task<IActionResult> Index(string? returnUrl)
    {
        var vm = await BuildViewModelAsync();
        vm.ReturnUrl = ResolveOpenerReturnUrl(returnUrl);
        return View(vm);
    }

    [HttpPost("/contact")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(ContactViewModel form)
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;

        if (!ContactEnquiryTypes.IsValidFor(form.EnquiryType, isAuthenticated))
        {
            ModelState.AddModelError(nameof(ContactViewModel.EnquiryType), "Select what you need help with");
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = 1,
                ErrorCodes = [ValidationErrorCoding.NoSelection],
                ErrorFields = [nameof(ContactViewModel.EnquiryType)],
            });

            var redisplay = await BuildViewModelAsync();
            redisplay.ReturnUrl = LocalUrl.SafeOrNull(form.ReturnUrl);
            redisplay.EnquiryType = form.EnquiryType;
            redisplay.Name = form.Name;
            redisplay.Email = form.Email;
            redisplay.School = form.School;
            redisplay.Details = form.Details;
            return View("Index", redisplay);
        }

        await analytics.TrackSafeAsync(new ContactUsSubmittedEvent
        {
            EnquiryType = form.EnquiryType!,
            IsAuthenticated = isAuthenticated,
        });

        // Audit logging of the activity only — never the name/email/school/message inputs.
        logger.LogInformation(
            "Contact Us submitted. Authenticated={IsAuthenticated} Laestab={Laestab} EnquiryType={EnquiryType}",
            isAuthenticated,
            isAuthenticated ? currentUser.OrganisationLaestab : string.Empty,
            form.EnquiryType);

        TempData["ContactUsSubmitted"] = true;
        return Redirect(ResolveExitTarget(form.ReturnUrl));
    }

    private async Task<ContactViewModel> BuildViewModelAsync()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var highlight = await contentBlocks.GetByKeyAsync(HighlightBlockKey);
        var vm = new ContactViewModel
        {
            IsAuthenticated = isAuthenticated,
            EnquiryOptions = ContactEnquiryTypes.ForAudience(isAuthenticated),
            HasHighlightContent = !string.IsNullOrWhiteSpace(highlight?.Value),
        };
        if (isAuthenticated)
        {
            vm.OrganisationName = currentUser.OrganisationName;
            vm.OrganisationLaestab = currentUser.OrganisationLaestab;
        }
        return vm;
    }

    // Capture the opener at GET time: the returnUrl param, else the same-origin GET Referer (the
    // linking page). Any /contact* path is excluded so the exit never loops back to the form.
    private string? ResolveOpenerReturnUrl(string? returnUrlParam)
    {
        var fromParam = LocalUrl.SafeOrNull(returnUrlParam);
        if (fromParam is not null && !IsContactPath(fromParam)) return fromParam;

        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrEmpty(referer)
            && Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            var local = LocalUrl.SafeOrNull(uri.PathAndQuery);
            if (local is not null && !IsContactPath(local)) return local;
        }
        return null;
    }

    private static string ResolveExitTarget(string? returnUrl)
    {
        var safe = LocalUrl.SafeOrNull(returnUrl);
        return safe is not null && !IsContactPath(safe) ? safe : GuidanceLanding;
    }

    private static bool IsContactPath(string path) =>
        path.Equals("/contact", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/contact/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/contact?", StringComparison.OrdinalIgnoreCase);
}
