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
    public string? ConflictErrorLink { get; init; }
    /// <summary>True when the summary was opened from the bulk review page: link back there and hide the submit/save actions.</summary>
    public bool FromBulk { get; init; }
    /// <summary>True when the summary was opened by editing a single request from the Amendment Requests page: link back there (submit/save actions stay).</summary>
    public bool FromEdit { get; init; }
    public string? PrimaryPupilPageId { get; init; }
    public string? FirstRecordDisplay { get; init; }
    public string? SecondRecordDisplay { get; init; }
    public string? MatchedPupilPageId { get; init; }
    public bool BackPageIsPupilSearch { get; init; }

    /// <summary>The JourneyController action that serves <see cref="BackPageId"/>.</summary>
    public string BackPageAction { get; init; } = nameof(JourneyController.Page);

    /// <summary>
    /// AB#296648: the enquiry-shaped summary data, set only for a results enquiry. Its presence is
    /// what switches <see cref="Lines"/> onto the enquiry row set.
    /// </summary>
    public ResultsEnquirySummary? Enquiry { get; init; }

    /// <summary>
    /// AB#297848: the missing-qualification enquiry summary, set only for that journey. Its
    /// presence switches <see cref="Lines"/> onto the missing-qualification row set, alongside
    /// <see cref="Enquiry"/> for incorrect-grade.
    /// </summary>
    public MissingQualificationSummary? MissingQualification { get; init; }

    /// <summary>
    /// True when this journey is a 16-19 results enquiry rather than a pupil-data amendment — the
    /// switch for the enquiry heading, the no-drafts rule and the cancel link. Resolved through the
    /// checking-exercise map rather than the presence of <see cref="Enquiry"/> /
    /// <see cref="MissingQualification"/>: each shape's builder guards on one named enum member, so
    /// shape-presence would silently render a future third enquiry journey as an amendment (the
    /// AB#297848 Back-link bug class). Mirrors <see cref="PageViewModel.IsResultsEnquiry"/>.
    /// </summary>
    public bool IsResultsEnquiry =>
        Application.WindowManagement.WhatToChangeCheckingExerciseMap.CheckingExerciseFor(WhatToChange)
            == Domain.Enums.CheckingExerciseType.ResultsEnquiry;

    public int TotalPagesUsed => FileRows.Sum(r => r.PageCount);

    public string WhatToChangeLabel => WhatToChange switch
    {
        WhatToChange.Remove => "Remove a pupil from data",
        WhatToChange.Include => "Include a pupil in data",
        WhatToChange.Merge => "Merge duplicate pupil records",
        WhatToChange.Add => "Add a pupil to data",
        _ => WhatToChange.ToString()
    };

    public string WhatToChangeNoun => WhatToChange switch
    {
        WhatToChange.Remove => "removal",
        WhatToChange.Include => "inclusion",
        WhatToChange.Merge => "merge",
        WhatToChange.Add => "addition",
        _ => WhatToChange.ToString().ToLower()
    };

    /// <summary>
    /// The summary as a flat, ordered list of key/value rows plus their change-link target. Shared
    /// source of truth for rendering the summary list: the standalone journey summary (via the
    /// <c>_SummaryDetails</c> partial) and the bulk detailed-review cards both iterate this. The
    /// GOV.UK summary-list/card tag helpers pass their context through <c>TagHelperContext.Items</c>,
    /// which does not cross a partial boundary, so the rows must be emitted inline in each view —
    /// only this data is shared, not the markup.
    /// </summary>
    public IReadOnlyList<SummaryLine> Lines
    {
        get
        {
            if (MissingQualification is { } missingQualification) return missingQualification.Lines;
            if (Enquiry is { } enquiry) return enquiry.Lines;

            var lines = new List<SummaryLine>
            {
                new("What pupil data would you like to change?", WhatToChangeLabel, null, false, null)
            };

            if (SecondRecordDisplay is not null)
            {
                lines.Add(new("First record to merge", FirstRecordDisplay ?? "", PrimaryPupilPageId, false, "first record to merge"));
                lines.Add(new("Second record to merge", SecondRecordDisplay, MatchedPupilPageId, false, "second record to merge"));
            }
            else if (PrimaryPupilPageId is not null)
            {
                // Only a journey with a pupil-search page has somewhere for this row's Change link
                // to go. The Add journey (AB#297310) has none — the pupil is typed in — and its
                // first and last name rows below already carry their own Change links, so an
                // actionless duplicate of them adds nothing.
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
