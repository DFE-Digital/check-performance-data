using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Features.LandingPage;
using DfE.CheckPerformanceData.Domain.Entities;
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
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var organisationId = User.FindFirst("organisation_id")?.Value;

        var result = await mediator.Send(new GetLandingPageDataQuery(userId, organisationId));
     
        var landingPageViewModel = new LandingPageViewModel(
            result.OpenWindows.Select(w => new LandingPageWindowViewModel
                { Title = w.Title, EndDate = w.EndDate, KeyStage = w.KeyStage }),
            result.OrganisationName,
            result.OrganisationUrn,
            result.OrganisationLaestab, string.Join(',', result.KeyStages.Select(ks => ks.Title)));
        
        return View(landingPageViewModel);
    }
}