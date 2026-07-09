using System.Diagnostics;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class KeyStageController(ILogger<KeyStageController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/KeyStage.cshtml";
    
    [ActionName("New")]
    [HttpGet("admin/windows/key-stage")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        var model = new KeyStageItem()
        {
            KeyStages = Enum.GetValues<KeyStages>(),
            KeyStage = draft.KeyStage,
            PostUrl = "/admin/windows/key-stage"
        };
        
        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/key-stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(KeyStageItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }
       
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }

        draft.KeyStage = model.KeyStage;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        if (draft.IsValid)
        {
            return RedirectToAction("New", "CreateCheckingWindow");
        }
        return RedirectToAction("New", draft.NextController(Url));
    }

}