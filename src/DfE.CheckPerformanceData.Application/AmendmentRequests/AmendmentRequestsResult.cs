using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestsResult
{
    public required string WindowTitle { get; init; }

    /// <summary>
    /// The window's word for a learner — "student" on 16-19. The grid holds both populations of a
    /// single window, so one noun is right for the whole page.
    /// </summary>
    public required LearnerNoun LearnerNoun { get; init; }

    /// <summary>
    /// One deadline per checking exercise the window runs, in sort order (#320). The page used to
    /// print the outer window's end date, which on a 16-19 window is the results-enquiry close —
    /// months after pupil data checking shuts. Since the grid holds both populations, one date
    /// could not be right for both.
    /// </summary>
    public required IReadOnlyList<ExerciseDeadlineDto> Deadlines { get; init; }
    public required IReadOnlyList<AmendmentRequestDto> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestDto> SubmittedRows { get; init; }

    /// <summary>The Issues tab's rows (AB#298325), after any pupil-name search filter.</summary>
    public required IReadOnlyList<ResultsEnquiryIssueDto> IssueRows { get; init; }

    /// <summary>
    /// Whether the school has ANY submitted enquiries for this window, before search filtering.
    /// The view needs both facts: no-issues-at-all shows the empty state, issues-but-no-match
    /// shows a no-results message — telling a school with enquiries "there are none" would send
    /// them off to raise a duplicate, the exact thing the ticket exists to prevent.
    /// </summary>
    public required bool HasAnyIssues { get; init; }
}

/// <summary>When one of the window's checking exercises closes, and whether it still has.</summary>
public sealed class ExerciseDeadlineDto
{
    public required CheckingExerciseType Exercise { get; init; }
    public required DateTime EndDate { get; init; }

    /// <summary>False once the deadline has passed, so the page can say so in the past tense.</summary>
    public required bool IsOpen { get; init; }
}
