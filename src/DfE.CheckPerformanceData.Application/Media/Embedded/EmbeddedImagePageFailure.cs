namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>A page the move left as it was, and why.</summary>
public sealed record EmbeddedImagePageFailure(EmbeddedImagePage Page, IReadOnlyList<EmbeddedImageRefusal> Reasons);
