namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class EditableContentViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string ValueHtml { get; init; } = string.Empty;
    public bool IsEditing { get; init; }
    public bool HasSavedContent { get; init; }
    public string ReturnUrl { get; init; } = "/";
}
