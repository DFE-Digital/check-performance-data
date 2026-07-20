namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class EditAdviceViewModel
{
    public required Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }
    public required string PupilName { get; init; }
    public required string AdviceText { get; init; }
    public IReadOnlyList<string> EvidenceMessages { get; init; } = [];

    /// <summary>The removal reason's display label, shown only for Remove requests; null otherwise.</summary>
    public string? ReasonForRemoval { get; init; }

    /// <summary>Where the Back link returns to (Amendment requests index, or the bulk review page when editing from a batch).</summary>
    public required string BackUrl { get; init; }
}
