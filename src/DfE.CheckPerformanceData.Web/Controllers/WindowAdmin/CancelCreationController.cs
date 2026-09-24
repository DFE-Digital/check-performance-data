using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.NewWindow)]
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
    public IActionResult Submit(string action)
    {
        // Matches the Delete button's value in CheckingWindow.cshtml.
        if (action == "delete")
        {
            HttpContext.Session.RemoveObject("CheckingWindowDraft");
            return RedirectToAction("Index", "Admin");
        }

        // Continue: resume from the session draft. The form posts no answers, so the draft must
        // come from session, not from model binding.
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");
        if (draft is null)
        {
            return RedirectToAction("Index", "Admin");
        }

        return Redirect(draft.NextController(Url));
    }
}