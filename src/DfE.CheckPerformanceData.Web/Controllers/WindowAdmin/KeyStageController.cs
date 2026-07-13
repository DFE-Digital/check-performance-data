using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class KeyStageController(IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/KeyStage.cshtml";
    
    [HttpGet("admin/windows/key-stage")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        KeyStageItem model = new KeyStageItem()
        {
            KeyStages = Enum.GetValues<KeyStages>(),
            KeyStage = draft.KeyStage,
            PostUrl = Url.Action("Submit", "KeyStage"),
            CancelUrl = Url.Action("Index", "CancelCreation")
        };
        
        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/key-stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(KeyStageItem model, CancellationToken cancellationToken)
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
        return Redirect(draft.NextController(Url));
    }

}