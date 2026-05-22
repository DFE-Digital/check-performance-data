using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class QuestionPartialModel
{
    public Guid WindowId { get; init; }
    public required string PageId { get; init; }
    public required Question Question { get; init; }
    public QuestionAnswer? ExistingAnswer { get; init; }
    public bool FromSummary { get; init; }
    public bool IsPageHeading { get; init; }
    public string? Error { get; init; }
    public string? UploadError { get; init; }

    public string FieldName => $"q_{Question.Id.Replace("-", "_")}";
    public string ErrorFieldRef => Question.Type == QuestionType.Date ? $"{FieldName}_day" : FieldName;
    public bool HasError => Error is not null;
    public string ResolvedTitle { get; init; } = string.Empty;

    // File upload computed properties
    public IReadOnlyList<FileAnswer> UploadedFiles => ExistingAnswer?.FileValues ?? [];
    public int TotalPages => UploadedFiles.Sum(f => f.PageCount);
    public bool AtLimit => TotalPages >= 6;
    public string DescribedBy => UploadError is not null ? "fileUpload-hint fileUpload-error" : "fileUpload-hint";
    public IReadOnlyList<FileUploadRow> UploadedFileRows => UploadedFiles.Select(f => new FileUploadRow(f)).ToList();
}

public sealed record FileUploadRow(FileAnswer File)
{
    public string FormattedSize => File.FileSizeBytes switch
    {
        < 1024 => $"{File.FileSizeBytes} bytes",
        < 1024 * 1024 => $"{File.FileSizeBytes / 1024.0:F1} KB",
        _ => $"{File.FileSizeBytes / (1024.0 * 1024):F1} MB"
    };
}
