namespace DfE.CheckPerformanceData.Application.PageTree;

public sealed class PageNodeVersionDto
{
    public Guid Id { get; init; }
    public int VersionId { get; init; }
    public int MinorVersion { get; init; }
    public bool IsCurrent { get; init; }

    // The version that replaced this one, or null. A replaced version is never live, scheduled
    // or the working draft.
    public Guid? SupersededById { get; init; }

    // Staging import only: the VersionId, in the same page, of the version that replaced this one.
    public int? SupersededByVersionId { get; init; }

    public bool IsSuperseded => SupersededById is not null;
    public DateTime? PublishFrom { get; init; }
    public DateTime? PublishTo { get; init; }
    public required string Content { get; init; }

    // Search-friendly plain-text extract of the version's content. Optional on read paths that do
    // not need it; carried on staging export/import so the target does not have to re-derive it.
    public string BodyPlainText { get; init; } = string.Empty;

    // Staging import only: the media library version each image in this version shows, by image
    // id. Null, or an image left out, shows the image's current version. Read paths leave it null.
    public IReadOnlyDictionary<Guid, int>? MediaPins { get; init; }

    public DateTime CreatedDate { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime UpdatedDate { get; init; }
    public string? UpdatedBy { get; init; }
}
