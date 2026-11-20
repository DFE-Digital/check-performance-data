namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// How a page shows its library images, passed to the Image widget through ViewData.
/// <see cref="PageVersionId"/> names a historic page version whose recorded image versions to show,
/// or is null for the current versions. <see cref="ShowEditorNotices"/> is true in the editor and
/// in previews, where a missing image shows a warning instead of nothing.
/// A view that sets no context renders as a public page.
/// </summary>
public sealed record MediaRenderContext(Guid? PageVersionId, bool ShowEditorNotices)
{
    public const string ViewDataKey = "MediaRenderContext";

    public static readonly MediaRenderContext Public = new(null, false);
}
