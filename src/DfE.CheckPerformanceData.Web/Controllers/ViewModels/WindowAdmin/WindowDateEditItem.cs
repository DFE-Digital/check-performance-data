using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class WindowDateEditItem : AdminPage
{
    [Required(ErrorMessage = "Date can not be empty")]
    public DateTime? DateValue { get; init; }
}