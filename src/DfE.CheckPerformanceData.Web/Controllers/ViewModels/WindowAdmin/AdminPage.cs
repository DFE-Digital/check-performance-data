namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class AdminPage
{
    public Guid WindowId { get; init; }
    public string? PostUrl { get; set; } = string.Empty;
    public string? CancelUrl { get; init; } = string.Empty; 
}