using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class WindowTitleEditItem: AdminPage
{
    [Required(ErrorMessage = "Title can not be empty"), 
     MaxLength(200, ErrorMessage = "Title must be 200 characters or less")]
    public string? Title { get; init; }
}