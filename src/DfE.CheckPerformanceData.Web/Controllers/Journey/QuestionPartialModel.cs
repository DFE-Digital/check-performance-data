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
    /// <summary>
    /// The options to render. For a Radio these are the config's options after visibility filtering;
    /// for a <see cref="QuestionType.GradeSelect"/> they are the qualification's grades, pass before
    /// fail, built from the AODC reference data.
    /// </summary>
    public IReadOnlyList<QuestionOption> VisibleOptions { get; init; } = [];

    /// <summary>
    /// True when this is a grade picker with nothing to pick — which can only mean the result's QAN
    /// is absent from the AODC reference data, since every qualification in that data has at least one
    /// grade. Drives the "we cannot list grades for this qualification yet" message. AB#297130.
    /// </summary>
    public bool GradeOptionsUnavailable =>
        Question.Type == QuestionType.GradeSelect && VisibleOptions.Count == 0;

    /// <summary>
    /// True when this is a syllabus-code picker with nothing to pick — the QAN is one of the 961
    /// (of 974) QualList entries with no 16-19 syllabus rows in the SyllabusCodes export. AB#297848.
    /// </summary>
    public bool SyllabusOptionsUnavailable =>
        Question.Type == QuestionType.SyllabusSelect && VisibleOptions.Count == 0;

    public int MaxEvidencePages { get; init; }

    /// <summary>
    /// Every evidence file name already uploaded anywhere in this request (AB#296081) —
    /// rendered as data-existing-file-names on the file input for the selection-time
    /// duplicate warning. Request-wide, not per-question, to match the server rule.
    /// </summary>
    public IReadOnlyList<string> ExistingFileNames { get; init; } = [];

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
