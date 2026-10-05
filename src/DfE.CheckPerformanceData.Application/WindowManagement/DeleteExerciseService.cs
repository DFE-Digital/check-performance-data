using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="IDeleteExerciseService"/>
public sealed class DeleteExerciseService(
    IExerciseDeletionRepository repository,
    IWindowBlobStorage blobStorage) : IDeleteExerciseService
{
    public async Task<DeleteExercisePreview> PreviewAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var byStatus = await repository.CountRequestsByStatusAsync(exerciseId, cancellationToken);
        int Count(params RequestStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));
        // #536: a submitted request's Status stays Submitted; whether it has gone to Zendesk is its
        // ProcessingStatus, which the repository counts separately.
        var sent = await repository.CountSentForProcessingAsync(exerciseId, cancellationToken);

        return new DeleteExercisePreview
        {
            SubmittedNotSent = Count(RequestStatus.Submitted) - sent,
            SentForProcessing = sent,
            Drafts = Count(RequestStatus.InProgress, RequestStatus.ReadyToSubmit),
            WithdrawnOrCancelled = Count(RequestStatus.Withdrawn, RequestStatus.NotSubmitted)
        };
    }

    public async Task<bool> DeleteAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        // The rows go first, for the same reason as DeleteWindowService: blobs that nothing points
        // at are harmless, but an exercise row whose files are gone still shows to schools.
        var deleted = await repository.DeleteAsync(windowId, exerciseId, cancellationToken);
        if (deleted is null)
            return false;

        // An older exercise's files are under prefixes named by its kind, which a new exercise of
        // the same kind can also read. Nothing says which of those blobs are its own, so they stay.
        if (deleted.UsesExerciseStorage)
            await blobStorage.DeleteExerciseBlobsAsync(windowId, exerciseId, cancellationToken);

        return true;
    }
}
