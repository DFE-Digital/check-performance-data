using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class CheckYourPupilDataController(ICurrentUserService currentUserService, IDfESignInApiClient apiClient, PortalDbContext dbContext) : Controller
{
    [Route("CheckYourPupilData/{windowId}")]
    public async Task<IActionResult> Index(string windowId)
    {
        // Get this organisations pupil data.

        var orgId = currentUserService.OrganisationId;
        var org = await apiClient.GetOrganisationAsync(currentUserService.UserId, orgId);

        var pupils = await dbContext.Pupils.Where(p => p.Urn == org.Urn && p.CheckingWindowId = Guid.Parse(windowId));
        
        return View();
    }
}