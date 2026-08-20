namespace DfE.CheckPerformanceData.Application.Notify;

/// <summary>
/// Self-contained, serializable description of a notification email to send.
/// Built on the request thread (capturing the request-scoped values it needs) and
/// handed to <see cref="INotificationDispatcher"/> so the actual send happens off the
/// request thread. Because it carries everything the send requires, it is already in the
/// shape needed to become a durable queue payload in a future migration.
/// </summary>
public sealed record EmailNotification
{
    /// <summary>Which notification template/scenario to send.</summary>
    public required NotificationType Type { get; init; }

    /// <summary>Submission reference number.</summary>
    public required string ReferenceNumber { get; init; }

    /// <summary>Pre-formatted, display-friendly deadline text (empty when not applicable).</summary>
    public string Deadline { get; init; } = string.Empty;

    /// <summary>Optional pre-generated link URL (must be produced on the request thread).</summary>
    public string? LinkUrl { get; init; }

    /// <summary>UKPRN used to resolve organisation recipients when applicable.</summary>
    public required string Ukprn { get; init; }

    /// <summary>The signed-in user's email; always a recipient.</summary>
    public required string OriginatorEmail { get; init; }

    /// <summary>
    /// When true, organisation users (resolved via DfE Sign-in) are added to the recipient
    /// set in addition to the originator. When false, only the originator is notified.
    /// </summary>
    public bool IncludeOrganisationUsers { get; init; }

    /// <summary>
    /// For a consolidated bulk submission email: every reference in the batch. Null/empty for
    /// single-reference notifications (which use <see cref="ReferenceNumber"/>).
    /// </summary>
    public IReadOnlyList<string>? ReferenceNumbers { get; init; }

    /// <summary>Checking-exercise name shown in the email body (template <c>((ce name))</c>).</summary>
    public string CeName { get; init; } = string.Empty;

    /// <summary>"Student" for Post 16 windows, otherwise "Pupil" (template <c>((learner noun))</c>).</summary>
    public string LearnerNoun { get; init; } = string.Empty;

    /// <summary>
    /// Per-window turnaround commitment phrase (template <c>((turnaround commitment))</c>).
    /// Empty = not configured; the personalisation key is omitted (FR-006).
    /// </summary>
    public string TurnaroundCommitment { get; init; } = string.Empty;
}
