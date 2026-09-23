using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.Egress;

/// <summary>The runs history page (AB#294590). Read-only: filters, one page of rows, the pager's numbers.</summary>
public sealed class EgressRunsViewModel
{
    /// <summary>Every checking window, newest start first, labelled as the Pull page labels them.</summary>
    public required IReadOnlyList<WindowChoice> Windows { get; init; }
    public Guid? SelectedWindowId { get; init; }
    public EgressRunOutcome? SelectedOutcome { get; init; }
    public required IReadOnlyList<EgressRunsRow> Rows { get; init; }
    /// <summary>1-based and already clamped by the repository.</summary>
    public required int Page { get; init; }
    public required int TotalPages { get; init; }
    public required int TotalCount { get; init; }
    /// <summary>Chooses between the two empty states: "no runs match" versus "no runs yet".</summary>
    public bool FiltersApplied => SelectedWindowId is not null || SelectedOutcome is not null;
    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>One table row. The link goes to the existing Resume action, which already routes a run to the right screen by status.</summary>
public sealed record EgressRunsRow(
    Guid Id,
    string WindowTitle,
    EgressRunOutcome Outcome,
    string OutcomeLabel,
    string OutcomeTagClass,
    IReadOnlyList<string> OutputTypeLabels,
    int RecordsTransferred,
    string StartedByName,
    DateTime StartedAtUtc)
{
    public bool CanResume => Outcome == EgressRunOutcome.Draft;
    /// <summary>Success lands on the read-only Complete page; Failed on its Failed page or Summary. Abandoned has nowhere to go.</summary>
    public bool CanView => Outcome is EgressRunOutcome.Success or EgressRunOutcome.Failed;
    // FLAGGED copy (AB#294590).
    public string? LinkText => CanResume ? "Resume" : CanView ? "View" : null;

    public static string TagClassFor(EgressRunOutcome outcome) => outcome switch
    {
        EgressRunOutcome.Success => "govuk-tag--green",
        EgressRunOutcome.Failed => "govuk-tag--red",
        EgressRunOutcome.Draft => "govuk-tag--blue",
        _ => "govuk-tag--grey"
    };
}
