namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class AdminSiteAssetsViewModel
{
    public string? Css { get; init; }
    public string? Js { get; init; }
    public bool CssEnabled { get; init; }
    public bool JsEnabled { get; init; }
    public string? Error { get; init; }
    public int MaxLength { get; init; } = 200_000;
}
