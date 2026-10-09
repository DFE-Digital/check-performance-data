using System.Text;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>
/// The one mapping from an audit row's EntityType/Action to what the audit log shows (AB#294592).
/// "Activity" on the page IS the row's EntityType; egress transfers, early closures (AB#301022) and automatic hand-overs (AB#302158) are the only rows with an outcome.
/// Labels are FLAGGED copy. An unmapped type is humanised ("PageNodeVersion" → "Page node
/// version") rather than shown raw, because the generic capture names every entity the app has.
/// </summary>
public static class AuditActivities
{
    /// <summary>EntityType of the row the egress transfer writes (see EgressRunRepository).</summary>
    public const string Egress = "EgressRun";
    /// <summary>EntityType the generic capture writes for a checking window; its EntityId is the window id.</summary>
    public const string CheckingWindow = "CheckingWindow";
    public const string TransferAction = "Transfer";
    public const string TransferFailedAction = "TransferFailed";
    /// <summary>
    /// EntityType of the hand-written rows window administration writes (AB#301022). Its EntityId
    /// is the window id, so the window filter matches it without reading the payload.
    /// </summary>
    public const string WindowAdmin = "WindowAdmin";
    /// <summary>An admin closed a checking exercise before its scheduled end.</summary>
    public const string ClosedEarlyAction = "ClosedEarly";
    /// <summary>
    /// AB#302158: the service handed an exercise's requests over for processing by itself, two
    /// hours after the exercise ended. Written only for a run that sent or cancelled something.
    /// </summary>
    public const string RequestsSentAutomaticallyAction = "RequestsSentAutomatically";

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Egress] = "Data egress",
        [CheckingWindow] = "Checking window",
        [WindowAdmin] = "Window admin",
        ["CheckingExercise"] = "Checking exercise",
        ["CheckingWindowDataset"] = "Checking window dataset",
        ["ChangeRequest"] = "Amendment request",
        ["ContentBundle"] = "Content import",
        ["PageNode"] = "Content page",
        ["PageNodeVersion"] = "Content page version",
        ["ContentBlock"] = "Content block",
        ["ContentBlockVersion"] = "Content block version",
        ["HomeBanner"] = "Home page banner",
        ["HomeBannerVersion"] = "Home page banner version",
        ["ContentStagingSession"] = "Content staging session",
        ["RulesConfigVersion"] = "Rules configuration",
        ["Setting"] = "System setting",
        ["AdminSectionAccess"] = "Role access",
        ["DlqMessage"] = "Dead letter queue",
        ["DeadLetterEntity"] = "Dead letter",
        ["QueueMessageEntity"] = "Queue message",
        ["SearchMessage"] = "Feedback message",
        ["SearchSession"] = "Search session",
        ["SearchAnalyticsSink"] = "Search test data",
        ["ShareToken"] = "Share token",
        ["DevZendeskTicket"] = "Dev Zendesk ticket",
        ["Country"] = "Country"
    };

    public static IReadOnlyList<AuditOutcome> AllOutcomes { get; } = [AuditOutcome.Success, AuditOutcome.Failed];

    public static string Label(string entityType) =>
        Labels.TryGetValue(entityType, out var label) ? label : Humanise(entityType);

    public static AuditOutcome? OutcomeOf(string entityType, string action) => (entityType, action) switch
    {
        (Egress, TransferAction) => AuditOutcome.Success,
        (Egress, TransferFailedAction) => AuditOutcome.Failed,
        // AB#301022: written only once the exercise has closed, so it has no failed twin.
        (WindowAdmin, ClosedEarlyAction) => AuditOutcome.Success,
        // AB#302158: written only for a hand-over that happened; a failed run is retried, not recorded.
        (WindowAdmin, RequestsSentAutomaticallyAction) => AuditOutcome.Success,
        _ => null
    };

    public static string OutcomeLabel(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Success => "Success",
        AuditOutcome.Failed => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };

    /// <summary>Enum names only, case-insensitive. Enum.TryParse would also accept "0"/"1", which a hand-typed URL must not.</summary>
    public static AuditOutcome? TryParseOutcome(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        foreach (var outcome in AllOutcomes)
            if (string.Equals(outcome.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return outcome;
        return null;
    }

    // "PageNodeVersion" → "Page node version": a space before each upper-case letter that follows a
    // non-upper-case one, and every upper-case letter after the first lowered.
    private static string Humanise(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType)) return string.Empty;
        var sb = new StringBuilder(entityType.Length + 4);
        for (var i = 0; i < entityType.Length; i++)
        {
            var c = entityType[i];
            if (i == 0) { sb.Append(c); continue; }
            if (char.IsUpper(c))
            {
                if (!char.IsUpper(entityType[i - 1])) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
