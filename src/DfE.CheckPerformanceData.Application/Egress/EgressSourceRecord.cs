using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// One request as pulled: the decision Zendesk holds for it plus every CYPMD-held value the LDS
/// files can need. This is what the Results screen shows and what a saved run keeps (as JSON on
/// EgressRunOutput), so resuming never re-pulls. Nothing here is transformed — raw dates, raw
/// laestab, raw reason values — transformation is the preprocessing pipeline's job and is shown
/// to the user as it happens.
/// </summary>
public sealed record EgressSourceRecord
{
    public required Guid ChangeRequestId { get; init; }
    public required string ReferenceNumber { get; init; }
    public long? TicketId { get; init; }
    /// <summary>A Zendesk decision value, or EgressDecisions.NoTicket / NotFound.</summary>
    public required string Decision { get; init; }
    public required EgressOutputType OutputType { get; init; }
    public required CheckingWindowType WindowType { get; init; }
    public required DateTime SubmittedAtUtc { get; init; }
    public required long OrganisationUrn { get; init; }
    /// <summary>The school's LAESTAB as stamped on the request row at submit; null on rows written before the column existed.</summary>
    public string? OrganisationLaestab { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public string? PupilDateOfBirth { get; init; }
    public string? PupilSex { get; init; }
    /// <summary>UPN for KS4, ULN for Post16 (PupilDto.Identifier).</summary>
    public string? PupilIdentifier { get; init; }
    public string? PupilCypmdId { get; init; }
    public int PupilMatchRef { get; init; }
    public string? PupilLaestab { get; init; }
    public string? PupilEntryDate { get; init; }
    /// <summary>False when the journey blob could not be read — the record then fails validation with a clear reason.</summary>
    public bool JourneyFound { get; init; }
    /// <summary>The journey's answers flattened by question id (EgressAnswers.Flatten).</summary>
    public IReadOnlyDictionary<string, string> Answers { get; init; } = new Dictionary<string, string>();

    public string? Answer(string questionId) =>
        Answers.TryGetValue(questionId, out var value) ? value : null;
}

/// <summary>One record's failure at one preprocessing step. Every failure is shown to the ops user; one failure fails the batch.</summary>
public sealed record EgressRecordFailure(string Step, long? TicketId, string ReferenceNumber, string Field, string Reason);
