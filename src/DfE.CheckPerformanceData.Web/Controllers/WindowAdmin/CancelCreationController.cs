using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class CancelCreationController : Controller
{
    private const string PageView = "~/Views/WindowAdmin/CheckingWindow.cshtml";

    [HttpGet("admin/windows/cancel-creation")]
    public IActionResult Index()
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");
        if (draft is null || draft.IsEmpty)
        {
            return RedirectToAction("Index", "Admin");
        }

        draft.PostUrl = "/admin/windows/cancel-creation";
        ViewBag.Cancelation = true;
        return View(PageView, draft);
    }
    
    [HttpPost("admin/windows/cancel-creation")]
    public IActionResult Submit(CheckingWindowDraft draft, string action)
    {
        if (action == "cancel")
        {
            HttpContext.Session.RemoveObject("CheckingWindowDraft");
            return RedirectToAction("Index", "Admin");
        }
        
        return RedirectToAction("New", draft.NextController(Url));
    }
}