using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="ICloseExerciseService"/>
public sealed class CloseExerciseService(
    IAdminRequestsRepository repository,
    IRequestBlobClient requestBlobClient,
    IQueueService queueService) : ICloseExerciseService
{
    public async Task<CloseExercisePreview> PreviewAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        var rows = await repository.GetDecidedRequestsForExerciseAsync(windowId, exerciseId, cancellationToken);

        return new CloseExercisePreview
        {
            RequestsToClose = rows.Count,
            RequestsWaiting = await repository.CountWaitingRequestsForExerciseAsync(windowId, exerciseId, cancellationToken),
            DraftsToCancel = await repository.CountDraftsForExerciseAsync(windowId, exerciseId, cancellationToken)
        };
    }

    public async Task<CloseExerciseResult> CloseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        var rows = await repository.GetDecidedRequestsForExerciseAsync(windowId, exerciseId, cancellationToken);

        var enqueued = 0;
        foreach (var row in rows)
        {
            // The document the school submitted, saved by RequestService at submit (#536). The
            // service is not live, so every submitted amendment has one: a missing document is a
            // fault, and the rows not yet sent stay Decided for the next run.
            var document = await requestBlobClient.GetRequestAsync(row.WindowId, row.ReferenceNumber)
                ?? throw new InvalidOperationException(
                    $"No saved request document for Reference={row.ReferenceNumber}; it cannot be sent for processing.");

            // Decided -> TicketQueued and the message, in one transaction. The mark comes first: false
            // means another run queued it first (and counted it), so no second message is sent. The
            // ticket maker cannot see the message before the row says TicketQueued, and a failure
            // leaves the row Decided for the next run.
            var queued = false;
            await repository.ExecuteInTransactionAsync(async () =>
            {
                queued = await repository.MarkTicketQueuedAsync(row.ChangeRequestId, cancellationToken);
                if (queued)
                    await queueService.EnqueueAsync(QueueOptions.ZendeskQueue, document, cancellationToken);
            }, cancellationToken);

            if (queued)
                enqueued++;
        }

        // Undecided amendments are left as they are; the next run sends them once decided.
        var waiting = await repository.CountWaitingRequestsForExerciseAsync(windowId, exerciseId, cancellationToken);

        // Drafts for this exercise were never submitted: InProgress / ReadyToSubmit -> NotSubmitted.
        var draftsCancelled = await repository.MarkDraftsNotSubmittedForExerciseAsync(
            windowId, exerciseId, cancellationToken);

        return new CloseExerciseResult { Enqueued = enqueued, Waiting = waiting, DraftsCancelled = draftsCancelled };
    }
}
