namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>One current version of a page (Id is its row id), with its stored content.</summary>
public sealed record EmbeddedImageVersion(Guid Id, int VersionId, EmbeddedVersionState State, string Content);
