namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class SchemaItem : AdminPage
{
    public IFormFile? Schema { get; set; }
    public string? SchemaFile { get; set; } = string.Empty;

    /// <summary>Which of the window's datasets this upload belongs to, e.g. "pupils",
    /// "included", "nonincluded".</summary>
    public string Dataset { get; set; } = "pupils";

    /// <summary>Human label for the page heading, e.g. "Included pupils".</summary>
    public string DatasetLabel { get; set; } = "Pupils";
}
