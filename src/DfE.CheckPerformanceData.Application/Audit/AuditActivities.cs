using System.Text;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>
/// The one mapping from an audit row's EntityType/Action to what the audit log shows (AB#294592).
/// "Activity" on the page IS the row's EntityType; egress rows are the only ones with an outcome.
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

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Egress] = "Data egress",
        [CheckingWindow] = "Checking window",
        ["CheckingExercise"] = "Checking exercise",
        ["CheckingWindowDataset"] = "Checking window dataset",
        ["ChangeRequest"] = "Amendment request",
        ["ContentBundle"] = "Content import",
        ["PageNode"] = "Content page",
        ["PageNodeVersion"] = "Content page version",
        ["ContentBlock"] = "Content block",
        ["ContentBlockVersion"] = "Content block version",
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

    public static AuditOutcome? OutcomeOf(string entityType, string action) =>
        entityType != Egress ? null : action switch
        {
            TransferAction => AuditOutcome.Success,
            TransferFailedAction => AuditOutcome.Failed,
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
