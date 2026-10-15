using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="ICloseExerciseService"/>
/// <remarks>
/// AB#307773: the exercise and each of its extensions are swept separately, by school. Whether an
/// extension is open is asked of <see cref="ICheckingExerciseService"/>, the one clock. The
/// <see cref="TimeProvider"/> here never compares a date; it only stamps
/// <c>RequestsSentAt</c> once an extension's sweep has run.
/// </remarks>
public sealed class CloseExerciseService(
    IAdminRequestsRepository repository,
    IRequestBlobClient requestBlobClient,
    IQueueService queueService,
    IWindowRepository windows,
    ICheckingExerciseService checkingExercises,
    IExerciseExtensionRepository extensions,
    TimeProvider timeProvider) : ICloseExerciseService
{
    public async Task<CloseExercisePreview> PreviewAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken) =>
        await PreviewAsync(windowId, exerciseId,
            ExerciseFilter(await RowAsync(windowId, exerciseId, cancellationToken)), cancellationToken);

    public async Task<CloseExerciseResult> CloseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        var row = await RowAsync(windowId, exerciseId, cancellationToken);
        var result = await SweepAsync(windowId, exerciseId, ExerciseFilter(row), cancellationToken);

        // Every extension that has closed by its effective end was covered by this sweep, so it is
        // stamped sent (review finding 4): the summary's "Requests sent" tag and the extension's own
        // Send page then read right, and a later extension sweep sends nothing twice (the rows are
        // TicketQueued). A sweep that threw never gets here. A no-op for one stamped earlier.
        foreach (var extension in (row?.Extensions ?? []).Where(x => x.RequestsSentAt is null && checkingExercises.HasClosed(row!, x)))
            await extensions.MarkRequestsSentAsync(extension.Id, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return result;
    }

    public async Task<CloseExercisePreview> PreviewExtensionAsync(
        Guid windowId, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken) =>
        await PreviewAsync(windowId, exerciseId,
            await ExtensionFilterAsync(windowId, exerciseId, extensionId, cancellationToken), cancellationToken);

    public async Task<CloseExerciseResult> CloseExtensionAsync(
        Guid windowId, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken)
    {
        var result = await SweepAsync(windowId, exerciseId,
            await ExtensionFilterAsync(windowId, exerciseId, extensionId, cancellationToken), cancellationToken);

        // First sweep after the close: from here the exercise's own sweep may take anything left.
        // A sweep that threw never gets here, so its schools stay held until one succeeds. A no-op
        // when the extension was stamped by an earlier run.
        await extensions.MarkRequestsSentAsync(extensionId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return result;
    }

    // Schools the exercise's own sweep must leave alone: in an extension still open by its
    // effective end (an extension closed early keeps its schools until the exercise ends). An
    // extension that has closed is swept by whichever sweep runs next, the exercise's or its own,
    // so a hand-over that keeps failing never hides its schools from the manual Send (finding 4).
    private SchoolFilter ExerciseFilter(CheckingExerciseDto? row)
    {
        // No row (a missing window or exercise, or a data share) holds no extensions, so the sweep
        // runs as it did before them. The controllers 404 before getting here.
        if (row is null) return SchoolFilter.None;

        return SchoolFilter.Exclude(row.Extensions
            .Where(x => checkingExercises.IsOpen(row, x))
            .SelectMany(x => x.Laestabs));
    }

    private async Task<SchoolFilter> ExtensionFilterAsync(
        Guid windowId, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken)
    {
        // Unlike the exercise, there is no "everyone" to fall back on: sweeping an unknown
        // extension under SchoolFilter.None would send every school's requests.
        var row = await RowAsync(windowId, exerciseId, cancellationToken);
        var extension = row?.Extensions.SingleOrDefault(x => x.Id == extensionId)
            ?? throw new InvalidOperationException(
                $"No extension {extensionId} on exercise {exerciseId} of window {windowId}.");
        return SchoolFilter.Only(extension.Laestabs);
    }

    private async Task<CheckingExerciseDto?> RowAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken) =>
        (await windows.GetByIdAsync(windowId, cancellationToken))?.Exercises
            .SingleOrDefault(e => e.Id == exerciseId && e.ExerciseType is not null);

    private async Task<CloseExercisePreview> PreviewAsync(
        Guid windowId, Guid exerciseId, SchoolFilter schools, CancellationToken cancellationToken)
    {
        var rows = await repository.GetDecidedRequestsForExerciseAsync(windowId, exerciseId, schools, cancellationToken);

        return new CloseExercisePreview
        {
            RequestsToClose = rows.Count,
            RequestsWaiting = await repository.CountWaitingRequestsForExerciseAsync(windowId, exerciseId, schools, cancellationToken),
            DraftsToCancel = await repository.CountDraftsForExerciseAsync(windowId, exerciseId, schools, cancellationToken)
        };
    }

    private async Task<CloseExerciseResult> SweepAsync(
        Guid windowId, Guid exerciseId, SchoolFilter schools, CancellationToken cancellationToken)
    {
        var rows = await repository.GetDecidedRequestsForExerciseAsync(windowId, exerciseId, schools, cancellationToken);

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
        var waiting = await repository.CountWaitingRequestsForExerciseAsync(windowId, exerciseId, schools, cancellationToken);

        // Drafts for this exercise were never submitted: InProgress / ReadyToSubmit -> NotSubmitted.
        var draftsCancelled = await repository.MarkDraftsNotSubmittedForExerciseAsync(
            windowId, exerciseId, schools, cancellationToken);

        return new CloseExerciseResult { Enqueued = enqueued, Waiting = waiting, DraftsCancelled = draftsCancelled };
    }
}
