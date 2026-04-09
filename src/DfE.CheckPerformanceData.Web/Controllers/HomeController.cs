using System.Diagnostics;
using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using Microsoft.AspNetCore.Mvc;
using DfE.CheckPerformanceData.Web.Models;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class HomeController(IPortalDbContext context, QueueServiceClient queueServiceClient) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View();
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest()
    {
        var client = queueServiceClient.GetQueueClient("requests");

        await client.CreateIfNotExistsAsync();
        
        await client.SendMessageAsync($"Hello, World! Time is {DateTime.Now.ToShortTimeString()}");
        
        // Handle the POST here
        return RedirectToAction(nameof(Index));
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
