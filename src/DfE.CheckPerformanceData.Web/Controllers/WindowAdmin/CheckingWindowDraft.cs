using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class CheckingWindowDraft 
{
    public string PostUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string TitleLink(IUrlHelper url) => url.Action("NewTitle", "Title");
    public DateTime? StartDate { get; set; }
    public string StartDateLink(IUrlHelper url) => url.Action("NewStartDate", "StartDate");
    public DateTime? EndDate { get; set; }
    public string EndDateLink(IUrlHelper url) => url.Action("NewEndDate", "EndDate");
    public CheckingWindowType? CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink(IUrlHelper url) => url.Action("NewCheckingWindowType", "CheckingWindowType");
    public KeyStages? KeyStage { get; set; }
    public string KeyStageLink(IUrlHelper url) => url.Action("NewKeyStage", "KeyStage");

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
            (true, _, _, _, _) => url?.Action("NewTitle", "Title"),
            (false, true, _, _, _) => url?.Action("NewStartDate", "StartDate"),
            (false, _, true, _, _) => url?.Action("NewEndDate", "EndDate"),
            (false, _, _, true, _) => url.Action("NewWindowType",  "WindowType"),
            (false, _, _, _, true) => url.Action("NewKeyStage", "KeyStage"),
            _ => url.Action("CreateCheckingWindow", "CreateCheckingWindow")
        };
}
