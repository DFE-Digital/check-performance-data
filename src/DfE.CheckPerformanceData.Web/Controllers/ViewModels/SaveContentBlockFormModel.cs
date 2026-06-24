namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SaveContentBlockFormModel
{
    public string Key { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? OriginalValue { get; set; }
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Optional DOM id of the block being edited. When safe, it is appended to the
    /// post-save redirect as a fragment so the editor is returned to the same place
    /// on the page instead of being thrown back to the top.
    /// </summary>
    public string? Anchor { get; set; }
}
