using DfE.CheckPerformanceData.Application.LandingPage;
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
            result.OrganisationLaestab, 
            string.Join(',', result.KeyStages.Select(ks => ks.Title)), 
            result.OrganisationAddress);
        
        return View(landingPageViewModel);
    }
}

public class LandingPageViewModel(
    IEnumerable<LandingPageWindowViewModel> windows,
    string? organisationName,
    string? organisationUrn,
    string? organisationLaestab,
    string? keyStages,
    string address)
{
    public IEnumerable<LandingPageWindowViewModel> Windows { get; } = windows;
    public string? OrganisationName { get; } = organisationName;
    public string? OrganisationUrn { get; } = organisationUrn;
    public string? OrganisationLaestab { get; } = organisationLaestab;
    public string? KeyStages { get; } = keyStages;
    public string OrganisationAddress { get; } = address;
}

public class LandingPageWindowViewModel
{
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required Guid Id { get; init; }
}