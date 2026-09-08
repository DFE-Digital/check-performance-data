using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// AB#298317: the window's next-opportunity date. Optional — no [Required] — because an admin may
/// not know it yet, and clearing it must be allowed. An invalid date is rejected by the GOV.UK
/// date-input model binder, which adds the model error itself.
/// </summary>
public sealed class WindowNextOpportunityEditItem : AdminPage
{
    // The GOV.UK date-input tag helper renders "{DisplayName} must be a real date" from model
    // metadata — without this, an admin reads the C# property name instead (review F3).
    [Display(Name = "Next opportunity")]
    public DateTime? NextOpportunity { get; set; }
}
