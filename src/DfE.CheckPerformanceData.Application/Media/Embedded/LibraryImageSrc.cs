namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// The library image that replaces an embedded one: its id, the unsigned src that rich text stores,
/// and its size in pixels (each null when not known).
/// </summary>
public sealed record LibraryImageSrc(Guid Id, string Src, int? Width = null, int? Height = null);
