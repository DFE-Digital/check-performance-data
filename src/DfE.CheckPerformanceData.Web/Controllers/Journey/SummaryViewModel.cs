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
    public string? ConflictError { get; init; }
    /// <summary>True when the summary was opened from the bulk review page: link back there and hide the submit/save actions.</summary>
    public bool FromBulk { get; init; }
    public string? PrimaryPupilPageId { get; init; }
    public string? FirstRecordDisplay { get; init; }
    public string? SecondRecordDisplay { get; init; }
    public string? MatchedPupilPageId { get; init; }
    public bool BackPageIsPupilSearch { get; init; }

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

    /// <summary>
    /// The summary as a flat, ordered list of key/value rows plus their change-link target. Shared
    /// source of truth for rendering the summary list: the standalone journey summary (via the
    /// <c>_SummaryDetails</c> partial) and the bulk "Continue B" cards both iterate this. The
    /// GOV.UK summary-list/card tag helpers pass their context through <c>TagHelperContext.Items</c>,
    /// which does not cross a partial boundary, so the rows must be emitted inline in each view —
    /// only this data is shared, not the markup.
    /// </summary>
    public IReadOnlyList<SummaryLine> Lines
    {
        get
        {
            var lines = new List<SummaryLine>
            {
                new("What pupil data would you like to change?", WhatToChangeLabel, null, false, null)
            };

            if (SecondRecordDisplay is not null)
            {
                lines.Add(new("First record to merge", FirstRecordDisplay ?? "", PrimaryPupilPageId, false, "first record to merge"));
                lines.Add(new("Second record to merge", SecondRecordDisplay, MatchedPupilPageId, false, "second record to merge"));
            }
            else
            {
                lines.Add(new("Pupil name", PupilName, PrimaryPupilPageId, false, "pupil name"));
            }

            foreach (var row in Rows)
                lines.Add(new(row.ResolvedTitle, row.DisplayAnswer, row.Page.Id, true, row.ResolvedTitle.ToLower()));

            return lines;
        }
    }
}

/// <summary>
/// One key/value summary-list row. <see cref="ChangePageId"/> null means the row has no change link.
/// <see cref="IsPageChange"/> true routes the change link to the question page (with
/// <c>fromSummary</c>); false routes it to the pupil-search page.
/// </summary>
public sealed record SummaryLine(
    string Key, string Value, string? ChangePageId, bool IsPageChange, string? ChangeHiddenText)
{
    public bool HasChange => ChangePageId is not null;
}

public sealed class SummaryRow(JourneyPage page, Question question, QuestionAnswer? answer, string resolvedTitle)
{
    public JourneyPage Page { get; } = page;
    public Question Question { get; } = question;
    public QuestionAnswer? Answer { get; } = answer;
    public string ResolvedTitle { get; } = resolvedTitle;

    public string DisplayAnswer => Question.Type switch
    {
        QuestionType.Date when Answer?.DateValue is { } d => d.ToDisplayString(),
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
