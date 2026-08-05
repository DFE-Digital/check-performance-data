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
    public string ErrorFieldRef => Question.Type switch
    {
        QuestionType.Date => $"{FieldName}_day",
        QuestionType.FileUpload => "fileUpload",
        _ => FieldName
    };
    public bool HasError => Error is not null;
    public string ResolvedTitle { get; init; } = string.Empty;
    public IReadOnlyList<QuestionOption> VisibleOptions { get; init; } = [];

    public int MaxEvidencePages { get; init; }

    // File upload computed properties
    public IReadOnlyList<FileAnswer> UploadedFiles => ExistingAnswer?.FileValues ?? [];
    public int TotalPages => UploadedFiles.Sum(f => f.PageCount);
    public bool AtLimit => MaxEvidencePages > 0 && TotalPages >= MaxEvidencePages;
    // Only reference ids that _FileUpload.cshtml actually renders. Naming a missing element
    // in aria-describedby leaves a dangling reference that resolves to nothing, so a question
    // with no hint would silently announce no description at all.
    public string DescribedBy => string.Join(" ", new[]
    {
        Question.Hint is not null ? "fileUpload-hint" : null,
        UploadError is not null || Error is not null ? "fileUpload-error" : null
    }.Where(id => id is not null));
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
