using System.Security.Claims;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class SecretController : Controller
{
    private readonly IDfESignInApiClient _dfeSignInApiClient;

    public SecretController(IDfESignInApiClient dfeSignInApiClient)
    {
        _dfeSignInApiClient = dfeSignInApiClient;
    }
    
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var userid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var orgJson = User.FindFirst("organisation")?.Value ?? "{}";
        var org = JsonNode.Parse(orgJson);
        var orgId = org["id"]?.ToString() ?? string.Empty;
        
        var organisation = await _dfeSignInApiClient.GetOrganisationAsync(userid, orgId);

        var vm = new SecretViewModel()
        {
            UserName = User.FindFirstValue(ClaimTypes.GivenName) + " " + User.FindFirstValue(ClaimTypes.Surname),
            OrganisationName = organisation?.Name
        };
        
        return View(vm);
    }
}