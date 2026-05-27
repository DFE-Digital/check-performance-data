using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class SummaryViewModel
{
    public Guid WindowId { get; init; }
    public required WhatToChange WhatToChange { get; init; }
    public required string PupilName { get; init; }
    public required List<SummaryRow> Rows { get; init; }
    public required List<SummaryFileRow> FileRows { get; init; }
    public required string BackPageId { get; init; }
    public required int MaxEvidencePages { get; init; }
    public string? DebugJson { get; init; }

    public int TotalPagesUsed => FileRows.Sum(r => r.PageCount);

    public string WhatToChangeLabel => WhatToChange switch
    {
        WhatToChange.Remove => "Remove a pupil from data",
        WhatToChange.Include => "Include a pupil in data",
        WhatToChange.Merge => "Merge duplicate pupil records",
        _ => WhatToChange.ToString()
    };

    public string WhatToChangeNoun => WhatToChange switch
    {
        WhatToChange.Remove => "removal",
        WhatToChange.Include => "inclusion",
        WhatToChange.Merge => "merge",
        _ => WhatToChange.ToString().ToLower()
    };
}

public sealed class SummaryRow(JourneyPage page, Question question, QuestionAnswer? answer, string resolvedTitle)
{
    public JourneyPage Page { get; } = page;
    public Question Question { get; } = question;
    public QuestionAnswer? Answer { get; } = answer;
    public string ResolvedTitle { get; } = resolvedTitle;

    public string DisplayAnswer => Question.Type switch
    {
        QuestionType.Date when Answer?.DateValue is { } d =>
            $"{d.Day} {new DateTime(d.Year, d.Month, d.Day):MMMM yyyy}",
        QuestionType.Radio when Answer?.TextValue is { } v =>
            Question.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v,
        _ => Answer?.TextValue ?? string.Empty
    };
}

public sealed class SummaryFileRow(JourneyPage page, string originalFileName, long fileSizeBytes, int pageCount, string storedFileName)
{
    public JourneyPage Page { get; } = page;
    public string OriginalFileName { get; } = originalFileName;
    public int PageCount { get; } = pageCount;
    public string StoredFileName { get; } = storedFileName;

    public string FileType => Path.GetExtension(OriginalFileName).TrimStart('.').ToUpperInvariant();

    public string FormattedFileSize
    {
        get
        {
            var kb = fileSizeBytes / 1024.0;
            return $"{kb:F2}KB";
        }
    }
}
