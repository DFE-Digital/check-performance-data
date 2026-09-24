using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Egress;

namespace DfE.CheckPerformanceData.Web.Controllers.AuditLog;

public sealed record ActivityChoice(string Value, string Label);

/// <summary>The audit log page (AB#294592). Read-only: three filters, one page of rows, the pager's numbers, the export link.</summary>
public sealed class AuditLogViewModel
{
    /// <summary>Distinct entity types present (always incl. EgressRun), labelled, ordered by label.</summary>
    public required IReadOnlyList<ActivityChoice> Activities { get; init; }
    /// <summary>Every checking window, newest start first, labelled as the egress pages label them.</summary>
    public required IReadOnlyList<WindowChoice> Windows { get; init; }
    public string? SelectedActivity { get; init; }
    public Guid? SelectedWindowId { get; init; }
    public AuditOutcome? SelectedOutcome { get; init; }
    public required IReadOnlyList<AuditLogRowViewModel> Rows { get; init; }
    /// <summary>1-based and already clamped by the repository.</summary>
    public required int Page { get; init; }
    public required int TotalPages { get; init; }
    public required int TotalCount { get; init; }
    /// <summary>Chooses between the two empty states: "no records match" versus "no records yet".</summary>
    public bool FiltersApplied => SelectedActivity is not null || SelectedWindowId is not null || SelectedOutcome is not null;
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The selected filters (and optionally a page) as a query string, so paging and export never widen the list.</summary>
    public string Query(int? page = null)
    {
        var parts = new List<string>(4);
        if (SelectedActivity is { } activity) parts.Add($"activity={Uri.EscapeDataString(activity)}");
        if (SelectedWindowId is { } windowId) parts.Add($"windowId={windowId}");
        if (SelectedOutcome is { } outcome) parts.Add($"status={outcome}");
        if (page is { } p) parts.Add($"page={p}");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    public string ExportUrl => "/admin/audit-log/export" + Query();
    public string PageUrl(int page) => "/admin/audit-log" + Query(page);
}

/// <summary>
/// One table row. Egress transfer rows: the person from the payload, a turquoise "Data egress" tag,
/// the output types under the window, and a Success/Failed tag. An egress row with no outcome (the
/// generic capture's record of the pull, Action "Insert") keeps the turquoise tag with "Run started"
/// beneath and no status. Every other row: the sign-in subject id (there is no user directory to
/// name it), a grey activity tag with the action beneath, no status.
/// </summary>
public sealed record AuditLogRowViewModel(
    long Id,
    string EntityType,
    string EntityId,
    string Action,
    string UserLabel,
    string ActivityLabel,
    string ActivityTagClass,
    string? ActivityDetail,
    string WindowTitle,
    string? WindowDetail,
    DateTime TimestampUtc,
    string? OutcomeLabel,
    string? OutcomeTagClass)
{
    // FLAGGED copy (AB#294592): an egress row (or a window row) whose window no longer exists or
    // whose payload pre-dates the window field.
    public const string UnknownWindow = "Unknown window";

    public static AuditLogRowViewModel From(AuditLogRow row)
    {
        var isEgress = row.EntityType == AuditActivities.Egress;
        var carriesWindow = isEgress || row.WindowId is not null;
        return new AuditLogRowViewModel(
            row.Id,
            row.EntityType,
            row.EntityId,
            row.Action,
            row.UserName ?? row.UserId ?? "System",
            AuditActivities.Label(row.EntityType),
            isEgress ? "govuk-tag--turquoise" : "govuk-tag--grey",
            isEgress ? (row.Outcome is null ? EgressActionLabel(row.Action) : null) : row.Action,
            row.WindowTitle ?? (carriesWindow ? UnknownWindow : string.Empty),
            isEgress && row.OutputTypes.Count > 0 ? string.Join(", ", row.OutputTypes.Select(LabelOutputType)) : null,
            row.TimestampUtc,
            row.Outcome is { } outcome ? AuditActivities.OutcomeLabel(outcome) : null,
            row.Outcome switch
            {
                AuditOutcome.Success => "govuk-tag--green",
                AuditOutcome.Failed => "govuk-tag--red",
                _ => null
            });
    }

    private static string LabelOutputType(string raw) =>
        Enum.TryParse<EgressOutputType>(raw, ignoreCase: true, out var type) && Enum.IsDefined(type) ? EgressOutputTypes.Label(type) : raw;

    // FLAGGED copy (AB#294592): the generic capture records the run row's creation — the pull — as
    // EgressRun / Insert. It is egress activity with no outcome; any other outcome-less action shows raw.
    private static string EgressActionLabel(string action) => action == "Insert" ? "Run started" : action;
}
