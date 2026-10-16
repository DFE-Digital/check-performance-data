using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="ICloseExerciseService"/>
/// <remarks>
/// AB#307773: the exercise and each of its extensions are swept separately, by school. Whether an
/// extension is open is asked of <see cref="ICheckingExerciseService"/>, the one clock. The
/// <see cref="TimeProvider"/> here never compares a date; it only stamps
/// <c>RequestsSentAt</c> once a sweep has covered an extension's schools: the extension's own, or
/// the exercise's after the extension closed.
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
            HeldFilter(await RowAsync(windowId, exerciseId, cancellationToken)), cancellationToken);

    public async Task<CloseExerciseResult> CloseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        var row = await RowAsync(windowId, exerciseId, cancellationToken);

        // One reading of the clock decides both sets, before the sweep: an extension is either held
        // back (open) or covered by this sweep (closed), never both, so one that ends mid-sweep
        // cannot be stamped for schools the sweep left alone.
        var open = row is null ? [] : row.Extensions.Where(x => checkingExercises.IsOpen(row, x)).ToList();
        var covered = row is null
            ? []
            : row.Extensions.Where(x => x.RequestsSentAt is null && !open.Contains(x)).ToList();

        var result = await SweepAsync(windowId, exerciseId, HeldFilter(row, open), cancellationToken);

        // Every closed extension was covered by this sweep, so it is stamped sent:
        // the summary's "Requests sent" tag and the extension's own Send page then read right, and a
        // later extension sweep sends nothing twice (the rows are TicketQueued). A sweep that threw
        // never gets here. A no-op for one stamped earlier.
        foreach (var extension in covered)
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

        // Records that a sweep has covered this extension's schools (the stamp also tells the
        // summary and the Send page). The exercise's sweep holds these schools only while the
        // extension is open, so the stamp does not release them. A sweep that threw never gets
        // here, so the extension is not recorded as sent until one succeeds. A no-op when an
        // earlier run stamped it.
        await extensions.MarkRequestsSentAsync(extensionId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return result;
    }

    // Schools the exercise's own sweep must leave alone: in an extension still open by its
    // effective end (an extension closed early keeps its schools until the exercise ends). An
    // extension that has closed is swept by whichever sweep runs next, the exercise's or its own,
    // so a hand-over that keeps failing never hides its schools from the manual Send.
    private SchoolFilter HeldFilter(CheckingExerciseDto? row) =>
        HeldFilter(row, row?.Extensions.Where(x => checkingExercises.IsOpen(row, x)).ToList() ?? []);

    private static SchoolFilter HeldFilter(CheckingExerciseDto? row, IReadOnlyList<CheckingExerciseExtensionDto> open)
    {
        // No row (a missing window or exercise, or a data share) holds no extensions, so the sweep
        // runs as it did before them. The controllers 404 before getting here.
        if (row is null) return SchoolFilter.None;

        return SchoolFilter.Exclude(open.SelectMany(x => x.Laestabs));
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

    public Task<CloseExerciseResult> SweepSchoolsAsync(
        Guid windowId, Guid exerciseId, IReadOnlyCollection<string> laestabs, CancellationToken cancellationToken) =>
        SweepAsync(windowId, exerciseId, SchoolFilter.Only(laestabs), cancellationToken);

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
