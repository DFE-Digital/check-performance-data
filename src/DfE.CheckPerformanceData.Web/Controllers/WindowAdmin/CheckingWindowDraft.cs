using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class CheckingWindowDraft : AdminPage
{ 
    public string? Title { get; set; }
    public string TitleLink(IUrlHelper url) => url.Action("New", "Title");
    public DateTime? StartDate { get; set; }
    public string StartDateLink(IUrlHelper url) => url.Action("New", "StartDate");
    public DateTime? EndDate { get; set; }
    public string EndDateLink(IUrlHelper url) => url.Action("New", "EndDate");
    public CheckingWindowType? CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink(IUrlHelper url) => url.Action("New", "WindowType");
    public KeyStages? KeyStage { get; set; }
    public string KeyStageLink(IUrlHelper url) => url.Action("New", "KeyStage");

    public bool IsValid
    {
        get
        {
            if (IsEmpty || StartDate < DateTime.UtcNow.Date || EndDate < StartDate)
                return false;

            return true;
        }
    }
    
    public bool IsEmpty => 
        Title == null && !StartDate.HasValue && !EndDate.HasValue && !CheckingWindowType.HasValue && !KeyStage.HasValue;

    public string NextController(IUrlHelper url) => (
            Title is null,
            !StartDate.HasValue, 
            !EndDate.HasValue, 
            !CheckingWindowType.HasValue, 
            !KeyStage.HasValue   
        ) switch
        {
            (true, _, _, _, _) => url.Action("New", "Title"),
            (false, true, _, _, _) => url.Action("New", "StartDate"),
            (false, _, true, _, _) => url.Action("New", "EndDate"),
            (false, _, _, true, _) => url.Action("New",  "WindowType"),
            (false, _, _, _, true) => url.Action("New", "KeyStage"),
            _ => url.Action("CreateCheckingWindow", "CreateCheckingWindow")
        };
}
