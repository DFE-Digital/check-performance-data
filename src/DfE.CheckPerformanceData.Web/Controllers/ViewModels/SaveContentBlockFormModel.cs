namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SaveContentBlockFormModel
{
    public string Key { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? OriginalValue { get; set; }
    public string? ReturnUrl { get; set; }
}
