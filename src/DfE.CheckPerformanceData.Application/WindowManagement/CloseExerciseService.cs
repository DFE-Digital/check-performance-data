using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="ICloseExerciseService"/>
public sealed class CloseExerciseService(
    IAdminRequestsRepository repository,
    IRequestBlobClient requestBlobClient,
    IQueueService queueService) : ICloseExerciseService
{
    public async Task<CloseExercisePreview> PreviewAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        var rows = await repository.GetDecidedRequestsForExerciseAsync(windowId, exercise, cancellationToken);

        return new CloseExercisePreview
        {
            RequestsToClose = rows.Count,
            RequestsWaiting = await repository.CountWaitingRequestsForExerciseAsync(windowId, exercise, cancellationToken),
            DraftsToCancel = await repository.CountDraftsForExerciseAsync(windowId, exercise, cancellationToken)
        };
    }

    public async Task<CloseExerciseResult> CloseAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        var rows = await repository.GetDecidedRequestsForExerciseAsync(windowId, exercise, cancellationToken);

        var enqueued = 0;
        foreach (var row in rows)
        {
            // The document the school submitted, saved by RequestService at submit (#536). The
            // service is not live, so every submitted amendment has one: a missing document is a
            // fault, and the rows not yet sent stay Decided for the next run.
            var document = await requestBlobClient.GetRequestAsync(row.WindowId, row.ReferenceNumber)
                ?? throw new InvalidOperationException(
                    $"No saved request document for Reference={row.ReferenceNumber}; it cannot be sent for processing.");

            await queueService.EnqueueAsync(QueueOptions.ZendeskQueue, document, cancellationToken);

            // Decided -> TicketQueued. False means another run queued it first; that run counted it.
            if (await repository.MarkTicketQueuedAsync(row.ChangeRequestId, cancellationToken))
                enqueued++;
        }

        // Undecided amendments are left as they are; the next run sends them once decided.
        var waiting = await repository.CountWaitingRequestsForExerciseAsync(windowId, exercise, cancellationToken);

        // Drafts for this exercise were never submitted: InProgress / ReadyToSubmit -> NotSubmitted.
        var draftsCancelled = await repository.MarkDraftsNotSubmittedForExerciseAsync(
            windowId, exercise, cancellationToken);

        return new CloseExerciseResult { Enqueued = enqueued, Waiting = waiting, DraftsCancelled = draftsCancelled };
    }
}
