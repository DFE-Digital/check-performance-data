namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>The library image that replaces an embedded one: its id and the unsigned src that rich text stores.</summary>
public sealed record LibraryImageSrc(Guid Id, string Src);
