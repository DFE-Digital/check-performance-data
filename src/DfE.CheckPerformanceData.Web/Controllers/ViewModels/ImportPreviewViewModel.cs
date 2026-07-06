using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// The import preview page: the analysis of the uploaded bundle, plus the bundle itself carried in
// a hidden field so the confirm step can apply it without re-uploading or server-side state.
public sealed class ImportPreviewViewModel
{
    public ContentImportPreview Preview { get; init; } = new([], []);
    public string BundleJson { get; init; } = string.Empty;
}

// Posted by the confirm step: the bundle, the two global defaults (one for existing / colliding
// items and one for brand-new items), and per-item overrides. Per-item Skip is honoured for both
// new and existing items — you can pick a subset of the bundle to import.
public sealed class ImportConfirmFormModel
{
    public string? BundleJson { get; set; }

    // Default action for items already present in this environment (a stable-Id or path/key
    // match). Skip preserves the target; Replace overwrites; Fail throws on collision.
    public ContentImportMode GlobalMode { get; set; } = ContentImportMode.Skip;

    // Default action for items that don't exist in this environment yet. Replace means
    // "include as new"; Skip means "leave it out of this import". Provides the same
    // pick-a-subset behaviour for a bundle imported into an empty environment as it does for
    // one with existing content.
    public ContentImportMode GlobalNewMode { get; set; } = ContentImportMode.Replace;

    public List<CollisionDecisionInput> Decisions { get; set; } = [];
}

// One per-item override (both new and colliding items). A null Action means "use the appropriate
// global default" (GlobalMode for existing items, GlobalNewMode for new items).
public sealed class CollisionDecisionInput
{
    public Guid Id { get; set; }
    public ContentImportMode? Action { get; set; }
}
