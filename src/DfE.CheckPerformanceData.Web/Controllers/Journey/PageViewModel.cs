using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class PageViewModel
{
    public Guid WindowId { get; init; }
    public required JourneyPage Page { get; init; }
    public required Dictionary<string, QuestionAnswer> Answers { get; init; }
    public required IReadOnlyList<QuestionPartialModel> QuestionModels { get; init; }
    public string? BackPageId { get; init; }

    /// <summary>Superseded by <see cref="BackPageAction"/> for building the link; kept because it
    /// states a distinct fact (the previous page was a pupil search) that callers still assert.</summary>
    public bool BackPageIsPupilSearch { get; init; }

    /// <summary>The JourneyController action that serves <see cref="BackPageId"/>. A bool cannot
    /// express this now there are three page routes.</summary>
    public string BackPageAction { get; init; } = nameof(JourneyController.Page);
    public bool FromSummary { get; init; }

    /// <summary>
    /// Which journey this page belongs to. Used by the Back link on a first page, where there is no
    /// history to go back through: an amendment journey started at WhatToChange, a results enquiry at
    /// ResultIssue, and sending the user to the wrong one drops them into a different task.
    /// </summary>
    public Application.CheckYourPupilData.WhatToChange? WhatToChange { get; init; }

    /// <summary>
    /// AB#296648: the exam result a ResultDetails page is about, shown as a summary above the
    /// revised-grade picker so the user can see they are correcting the right one. Null on every
    /// other page type.
    /// </summary>
    public Application.ResultsEnquiry.StudentResultRecord? SelectedResult { get; init; }

    /// <summary>AB#297848: the qualification a QualificationDetails page is about, shown as a
    /// summary card above the questions. Null on every other page type.</summary>
    public Application.ResultsEnquiry.QualificationReference? SelectedQualification { get; init; }

    /// <summary>AB#297848: the pupil's CYPMD id, shown on the QualificationDetails summary card
    /// (ResultDetails gets it from SelectedResult.CypmdId instead — there is no result here).</summary>
    public string? CypmdId { get; init; }
    public string PupilName { get; init; } = string.Empty;
    public string? ContentKey { get; init; }
    public string? UploadError { get; init; }
    public string? AtLeastOneError { get; init; }

    public string? ResolvedTitle => string.IsNullOrEmpty(Page.Title) ? null : ResolveTemplate(Page.Title);
    public bool IsSingleQuestion => Page.Questions.Count == 1;

    // The browser <title> (and therefore analytics) must never contain the pupil name.
    // Prefer the author-supplied sanitised PageTitle; otherwise fall back to a
    // pupil-name-free version of the page title, then the single question's title.
    public string PageTitle =>
        Page.PageTitle
        ?? (string.IsNullOrEmpty(Page.Title) ? null : JourneyTemplate.Strip(Page.Title))
        ?? (Page.Questions.Count > 0 ? JourneyTemplate.Strip(Page.Questions[0].Title) : string.Empty);
    public bool HasErrors => QuestionModels.Any(q => q.Error is not null) || UploadError is not null || AtLeastOneError is not null;

    public IReadOnlyList<QuestionPartialModel> FileUploadModels =>
        QuestionModels.Where(q => q.Question.Type == QuestionType.FileUpload).ToList();

    public IReadOnlyList<QuestionPartialModel> NonFileUploadModels =>
        QuestionModels.Where(q => q.Question.Type != QuestionType.FileUpload).ToList();

    public QuestionAnswer? GetAnswer(string questionId) =>
        Answers.TryGetValue(questionId, out var a) ? a : null;

    public string ResolveTemplate(string template) =>
        template.Replace("{pupilName}", PupilName, StringComparison.OrdinalIgnoreCase);
}
