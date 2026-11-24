using DfE.CheckPerformanceData.Application.Media.Embedded;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// A page or a content block that shows an image one way. A page has <see cref="PageNodeId"/> and
/// the states of its current versions that show it this way. A block has
/// <see cref="ContentBlockId"/> and no states. <see cref="Location"/> is a
/// <see cref="MediaUsageLocations"/> value. <see cref="Count"/> is how many times one version
/// shows it this way.
/// </summary>
public sealed record MediaVariantUse(
    Guid? PageNodeId,
    int? ContentBlockId,
    string Name,
    string Location,
    int Count,
    IReadOnlyList<EmbeddedVersionState> States);
