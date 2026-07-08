using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class CreateCheckingWindowController(ILogger<CreateCheckingWindowController> logger, IWindowService windowService, IReadOnlyDictionary<string, BlobServiceClient> blobClients) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/CheckingWindow.cshtml";
    
    [ActionName("New")]
    [HttpGet("admin/windows/create-checking-window")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.Cancelation = false;
        
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        draft.PostUrl = "/admin/windows/create-checking-window";
        
        return View(PageView, draft);
    }
    
    [HttpPost("admin/windows/create-checking-window")]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        if (draft.IsValid == false)
        {
            return BadRequest("Invalid data");
        }

        CheckingWindowDto checkingWindowDto = new CheckingWindowDto() 
        {
             Title = draft.Title,
             StartDate = draft.StartDate.Value,
             EndDate = draft.EndDate.Value,
             CheckingWindowType = draft.CheckingWindowType.Value,
             KeyStage = draft.KeyStage.Value,
         };
        CheckingWindowDto window = await windowService.CreateAsync(checkingWindowDto, cancellationToken);
        
        //Temp patch to create the app container for file storage
        CreateWindowContainer(window.Id.ToString());
        return RedirectToAction("Index", "Summary", new { id = window.Id });
    }

    private void CreateWindowContainer(string id)
    {
        if (!blobClients.TryGetValue("app", out var appBlobClient))
        {
            logger.LogWarning("App storage client is not configured");
            Problem("App storage is not configured.");
        }
        appBlobClient.CreateBlobContainer(id);
    }
    
}