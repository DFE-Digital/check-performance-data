namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

// Minimal projection of a SubmittedUnCommitted ChangeRequests row needed to rebuild
// its RequestDocument for the quick-and-dirty "send to Zendesk" admin replay. The rest
// of the document is rehydrated from the persisted RequestState blob.
public sealed record ReplayRequestRow
{
    public required Guid ChangeRequestId { get; init; }
    public required Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }
    public required long OrganisationUrn { get; init; }
    public required Guid SubmittedById { get; init; }
    public required string SubmittedByName { get; init; }
}
