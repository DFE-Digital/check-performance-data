using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class LandingPageController(IMediator mediator) : Controller
{
    public async Task<IActionResult> Index()
    {
        
        var openWindowData = await mediator.Send(new GetOpenWindowsQuery());

        var viewModel = new LandingPageViewModel
        {
            OrganisationName = User.FindFirstValue("organisation_name"),
            OrganisationLaestab = User.FindFirstValue("organisation_laestab"),
            OrganisationUrn = User.FindFirstValue("organisation_urn"),
            CheckingWindows = 
        };
        
        return View(viewModel);
    }
}

public class CheckingWindowViewModel
{
}

public class LandingPageViewModel
{
    public string? OrganisationName { get; set; }
    public string? OrganisationLaestab { get; set; }
    public string? OrganisationUrn { get; set; }
    public CheckingWindowViewModel[] CheckingWindows { get; set; }
}