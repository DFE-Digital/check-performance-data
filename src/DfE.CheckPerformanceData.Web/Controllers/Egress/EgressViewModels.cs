using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.Egress;

/// <summary>The Pull form's posted fields.</summary>
public sealed class PullForm
{
    public Guid? WindowId { get; set; }
    public List<EgressOutputType> OutputTypes { get; set; } = [];
}

public sealed record WindowChoice(Guid Id, string Label);

public sealed class PullViewModel
{
    public required IReadOnlyList<WindowChoice> Windows { get; init; }
    public required IReadOnlyList<EgressOutputType> OutputTypes { get; init; }
    public Guid? SelectedWindowId { get; init; }
    public IReadOnlyList<EgressOutputType> SelectedOutputTypes { get; init; } = [];
    public string? WindowError { get; init; }
    public string? OutputTypesError { get; init; }
    /// <summary>One sentence per refused output type: who holds it and since when, or when it was transferred and by whom.</summary>
    public IReadOnlyList<string> Refusals { get; init; } = [];
    public string? PullError { get; init; }
    public IReadOnlyList<EgressRunListItem> SavedRuns { get; init; } = [];
    public IReadOnlyList<EgressRunListItem> CompletedRuns { get; init; } = [];
    public string? Banner { get; init; }
    /// <summary>True when ConnectionStrings:EgressStorage is absent — the view warns up front that a transfer will refuse.</summary>
    public bool StorageNotConfigured { get; init; }
    public bool IsValid => WindowError is null && OutputTypesError is null && Refusals.Count == 0 && PullError is null;
}

public sealed record RunPageViewModel
{
    public required EgressRunDto Run { get; init; }
    public required string WindowTitle { get; init; }
    public required CheckingWindowType WindowType { get; init; }
    public string TargetDescription { get; init; } = string.Empty;
    public string? TransferError { get; init; }
    public string? StreamUrl { get; init; }
    public IReadOnlyList<string> StepNames { get; init; } = EgressPreprocessor.StepNames;
    public string CurrentUserName { get; init; } = string.Empty;
    /// <summary>True when ConnectionStrings:EgressStorage is absent — the Summary warns before Confirm rather than after.</summary>
    public bool StorageNotConfigured { get; init; }
    /// <summary>The Results page's "raw data" columns — one shared set; a column a record has no value for renders blank.</summary>
    public static readonly IReadOnlyList<(string Header, Func<EgressSourceRecord, string> Value)> RawColumns =
    [
        ("Ticket ID", r => r.TicketId?.ToString() ?? ""),
        ("Reference", r => r.ReferenceNumber),
        ("Decision", r => EgressDecisions.Label(r.Decision)),
        ("Reason", r => r.Answer("reason") ?? ""),
        ("Key stage", r => EgressOutputTypes.KeyStageValue(r.WindowType)),
        ("DfE establishment number", r => string.IsNullOrWhiteSpace(r.PupilLaestab) ? r.OrganisationLaestab ?? "" : r.PupilLaestab),
        ("Surname", r => r.Answer("last-name") ?? r.PupilSurname ?? ""),
        ("Forename", r => r.Answer("first-name") ?? r.PupilFirstname ?? ""),
        ("Sex", r => r.Answer("sex") ?? r.PupilSex ?? ""),
        ("Date of birth", r => r.Answer("date-of-birth") ?? r.PupilDateOfBirth ?? ""),
        ("Admission date", r => r.Answer("admission-date") ?? r.PupilEntryDate ?? ""),
        ("Year group", r => r.Answer("year-group") ?? ""),
        ("SEN status", r => r.Answer("sen-status") ?? ""),
        ("UPN or ULN", r => r.Answer("upn") ?? r.PupilIdentifier ?? ""),
        ("CYPMD ID", r => r.PupilCypmdId ?? ""),
        ("LDS matched pupil ID", r => r.PupilMatchRef > 0 ? r.PupilMatchRef.ToString() : ""),
        ("School URN", r => r.OrganisationUrn.ToString()),
        ("Submitted", r => r.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm") + " UTC")
    ];
}

public sealed class PreviewViewModel
{
    public required Guid RunId { get; init; }
    public required EgressOutputType OutputType { get; init; }
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
}
