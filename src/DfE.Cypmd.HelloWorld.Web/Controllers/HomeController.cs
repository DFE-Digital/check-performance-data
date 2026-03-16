using System.Diagnostics;
using DfE.Cypmd.HelloWorld.Data;
using DfE.Cypmd.HelloWorld.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using DfE.Cypmd.HelloWorld.Web.Models;

namespace DfE.Cypmd.HelloWorld.Web.Controllers;

public class HomeController(PortalDbContext context) : Controller
{
    public async Task<IActionResult> Index()
    {
        var workflows = context.Workflows.ToList();

        var wf = new Workflow()
        {
            Id = 1,
            Name = "Dave"
        };
        
        context.Workflows.Add(wf);
        
        await context.SaveChangesAsync();
        
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
