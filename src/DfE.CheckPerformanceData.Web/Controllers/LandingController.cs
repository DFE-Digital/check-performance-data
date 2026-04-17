using DfE.CheckPerformanceData.Application.Features.LandingPage;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class LandingController(IMediator mediator) : Controller
{
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var result = await mediator.Send(new GetLandingPageDataQuery());
     
        var landingPageViewModel = new LandingPageViewModel(
            result.OpenWindows.Select(w => new LandingPageWindowViewModel
                { Title = w.Title, EndDate = w.EndDate }),
            result.OrganisationName,
            result.OrganisationUrn,
            result.OrganisationLaestab, string.Join(',', result.KeyStages.Select(ks => ks.Title)));
        
        return View(landingPageViewModel);
    }
}