namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>One stored file in the cms-media container: its key and its size in bytes.</summary>
public sealed record MediaBlobEntry(string Key, long SizeBytes);
