using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class CreateCheckingWindowController(ILogger<CreateCheckingWindowController> logger, IWindowService windowService, IReadOnlyDictionary<string, BlobServiceClient> blobClients) : Controller
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

        if (!draft.IsValid)
        {
            return BadRequest("Invalid data");
        }

        CheckingWindowDto checkingWindowDto = new CheckingWindowDto()
        {
             Title = draft.Title!,
             // Derived from the exercises, never typed by the admin (#319). CreateAsync re-derives
             // them anyway; they are set here because the DTO requires them.
             StartDate = draft.StartDate!.Value,
             EndDate = draft.EndDate!.Value,
             CheckingWindowType = draft.CheckingWindowType!.Value,
             KeyStage = draft.KeyStage!.Value,
             Exercises = draft.ToExerciseDtos()
         };
        CheckingWindowDto window = await windowService.CreateAsync(checkingWindowDto, cancellationToken);

        if (!CreateWindowContainer(window.Id.ToString()))
        {
            return Problem("App storage is not configured.");
        }

        return RedirectToAction("Index", "Summary", new { id = window.Id });
    }

    // False when there is no app storage client to create the window's blob container with. The
    // caller surfaces that rather than carrying on: every later step of the wizard writes into
    // that container, so a window without one is not usable.
    private bool CreateWindowContainer(string id)
    {
        if (!blobClients.TryGetValue("app", out BlobServiceClient? appBlobClient))
        {
            logger.LogWarning("App storage client is not configured");
            return false;
        }

        appBlobClient.CreateBlobContainer(id);
        return true;
    }
    
}