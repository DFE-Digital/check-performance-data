namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Deletes one checking exercise from a window: its datasets and releases, every change request
/// made through it, and its files in the window's container.
/// </summary>
/// <remarks>
/// The window and its other exercises stay. There is no undo, so the admin page asks for
/// <see cref="PreviewAsync"/> first and shows what will go. To hide an exercise and keep its data,
/// the admin clears Enable on the edit page instead.
/// </remarks>
public interface IDeleteExerciseService
{
    /// <summary>
    /// The change requests a delete would remove, without removing them. A count, not a
    /// reservation: a school can submit a request between the preview and the press.
    /// </summary>
    Task<DeleteExercisePreview> PreviewAsync(Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the exercise. False when the window has no exercise with that id. Irreversible.
    /// </summary>
    Task<bool> DeleteAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken);
}

/// <summary>The change requests made through one exercise, by what a delete loses.</summary>
public sealed record DeleteExercisePreview
{
    /// <summary>Submitted by a school and not yet sent for processing. A delete loses these outright.</summary>
    public required int SubmittedNotSent { get; init; }

    /// <summary>Already sent to Zendesk. The tickets stay in Zendesk; only this service's copy goes.</summary>
    public required int SentForProcessing { get; init; }

    /// <summary>In progress or ready to submit.</summary>
    public required int Drafts { get; init; }

    /// <summary>Withdrawn, or cancelled when the exercise closed.</summary>
    public required int WithdrawnOrCancelled { get; init; }

    public int TotalRequests => SubmittedNotSent + SentForProcessing + Drafts + WithdrawnOrCancelled;

    public bool HasRequests => TotalRequests > 0;
}
