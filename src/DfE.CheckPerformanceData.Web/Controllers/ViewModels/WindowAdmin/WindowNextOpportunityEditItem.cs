using System.ComponentModel.DataAnnotations;
using GovUk.Frontend.AspNetCore;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// AB#298317: the window's next-opportunity date. Optional — no [Required] — because an admin may
/// not know it yet, and clearing it must be allowed. An invalid date is rejected by the GOV.UK
/// date-input model binder, which adds the model error itself.
/// </summary>
public sealed class WindowNextOpportunityEditItem : AdminPage
{
    // The GOV.UK date-input binder renders "{prefix} must be a real date". Since
    // GovUk.Frontend.AspNetCore 5 the prefix must be explicit — the tag helper throws without one —
    // and Display keeps the same wording anywhere else the name is shown (review F3).
    [Display(Name = "Next opportunity")]
    [DateInput(ErrorMessagePrefix = "Next opportunity")]
    public DateTime? NextOpportunity { get; set; }
}
