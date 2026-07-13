using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class WindowDateEditItem : AdminPage
{
    [Required(ErrorMessage = "Date can not be empty")]
    public DateTime? DateValue { get; init; }
}