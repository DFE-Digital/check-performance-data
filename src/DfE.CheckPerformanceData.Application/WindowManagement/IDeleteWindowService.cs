namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Deletes a whole checking window: its exercises, datasets and releases, every change request
/// made through it, its egress runs, and its blob container.
/// </summary>
/// <remarks>
/// Made for environments that are set up by hand (preproduction), where an admin must be able to
/// remove a window that was set up wrong and start again. There is no undo, so the admin page asks
/// for <see cref="PreviewAsync"/> first and shows what will go.
/// </remarks>
public interface IDeleteWindowService
{
    /// <summary>
    /// What a delete would remove, without removing it. A count, not a reservation: a school can
    /// submit a request between the preview and the press, and the delete removes that one too.
    /// </summary>
    Task<DeleteWindowPreview> PreviewAsync(Guid windowId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the window. False when no window has that id. Irreversible.
    /// </summary>
    Task<bool> DeleteAsync(Guid windowId, CancellationToken cancellationToken);
}

/// <summary>The change requests and egress runs made through a window, by what a delete loses.</summary>
public sealed record DeleteWindowPreview
{
    /// <summary>Submitted by a school and not yet sent for processing. A delete loses these outright.</summary>
    public required int SubmittedNotSent { get; init; }

    /// <summary>Already sent to Zendesk. The tickets stay in Zendesk; only this service's copy goes.</summary>
    public required int SentForProcessing { get; init; }

    /// <summary>In progress or ready to submit.</summary>
    public required int Drafts { get; init; }

    /// <summary>Withdrawn, or cancelled when an exercise closed.</summary>
    public required int WithdrawnOrCancelled { get; init; }

    public required int EgressRuns { get; init; }

    public int TotalRequests => SubmittedNotSent + SentForProcessing + Drafts + WithdrawnOrCancelled;

    public bool HasRequests => TotalRequests > 0;
}
