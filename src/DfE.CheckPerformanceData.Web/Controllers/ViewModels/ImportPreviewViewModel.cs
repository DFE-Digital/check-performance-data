using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// The import preview page: the analysis of the uploaded bundle, plus the bundle itself carried in
// a hidden field so the confirm step can apply it without re-uploading or server-side state.
public sealed class ImportPreviewViewModel
{
    public ContentImportPreview Preview { get; init; } = new([], []);
    public string BundleJson { get; init; } = string.Empty;
}

// Posted by the confirm step: the bundle, the global default mode, and any per-collision overrides.
public sealed class ImportConfirmFormModel
{
    public string? BundleJson { get; set; }
    public ContentImportMode GlobalMode { get; set; } = ContentImportMode.Skip;
    public List<CollisionDecisionInput> Decisions { get; set; } = [];
}

// One per-collision override. A null Action means "use the global default".
public sealed class CollisionDecisionInput
{
    public Guid Id { get; set; }
    public ContentImportMode? Action { get; set; }
}
