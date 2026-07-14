namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class SchemaItem : AdminPage
{
    public IFormFile? Schema { get; set; }
    public string? SchemaFile { get; set; } = string.Empty;
}
