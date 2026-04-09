using System.Security.Claims;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class SecretController(IDfESignInApiClient dfeSignInApiClient) : Controller
{
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var userid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var orgJson = User.FindFirst("organisation")?.Value ?? "{}";
        var org = JsonNode.Parse(orgJson);
        var orgId = org["id"]?.ToString() ?? string.Empty;
        
        var organisation = await dfeSignInApiClient.GetOrganisationAsync(userid, orgId);

        var vm = new SecretViewModel()
        {
            UserName = User.FindFirstValue(ClaimTypes.GivenName) + " " + User.FindFirstValue(ClaimTypes.Surname),
            Organisation = organisation
        };
        
        return View(vm);
    }

    public async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        return SignOut(
            new AuthenticationProperties
            {
                RedirectUri = Url.Action("Index", "Home")
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme
        );
    }
}