namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>The words for one way of showing an image: a title, and its settings one to a line.</summary>
public sealed record MediaVariantDescription(string Title, IReadOnlyList<string> Details);
