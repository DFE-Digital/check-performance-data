namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class EditableTitleViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string HeadingLevel { get; init; } = "h1";
    public string CssClass { get; init; } = "govuk-heading-xl";
    public bool IsEditing { get; init; }
    public bool HasSavedContent { get; init; }
    public string ReturnUrl { get; init; } = "/";
}
