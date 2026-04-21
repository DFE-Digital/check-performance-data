using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class DataCheckController(PortalDbContext dbContext, ICurrentUserService userService) : Controller
{
    [Route("DataCheck/{windowId}")]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var window = await dbContext.CheckingWindows.Where(w => w.Id == windowId)
            .FirstOrDefaultAsync();
        
        return View(window);
    }

    [Route("DataCheck/{windowId}/{requestType}")]
    public async Task<IActionResult> Step(Guid windowId, RequestTypes requestType)
    {
        var steps = await dbContext.CheckingWindowSteps
            .Where(w => w.CheckingWindowId == windowId && w.RequestType == requestType)
            .ToListAsync();

        var request = new AmendmentRequest
        {
            OrganisationId = Guid.Parse(userService.OrganisationId),
            CheckingWindowId = windowId,
            CurrentStepIndex = 0,
            Status = AmendmentStatus.InProgress
        };
        
        
        
        return Ok(steps);
    }
}