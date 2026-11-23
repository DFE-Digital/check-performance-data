namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>A library image whose default alt text is missing or weak. It is fixed on the image's own page.</summary>
public sealed record MediaAltTextAssetRow(Guid AssetId, string FileName, string? Alt, AltTextProblem Problem, Guid? FolderId = null);
