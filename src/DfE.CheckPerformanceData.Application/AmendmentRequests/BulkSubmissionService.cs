using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class BulkSubmissionService(
    IRequestRepository requestRepository,
    IRequestService requestService,
    IRequestNotificationService requestNotificationService,
    ICheckYourPupilDataService checkYourPupilDataService,
    ICurrentUserService currentUserService,
    IAnalyticsService analytics) : IBulkSubmissionService
{
    private const string AlreadySubmittedReason = "A request for this pupil has already been submitted.";
    private const string SelectedMoreThanOnceReason = "You selected more than one request for this pupil.";

    private long OrganisationUrn => long.Parse(currentUserService.OrganisationUrn);

    public async Task<BulkReviewResult> BuildReviewAsync(Guid windowId, IReadOnlyList<string> selectedReferences)
    {
        var selected = new HashSet<string>(selectedReferences, StringComparer.Ordinal);
        var urn = OrganisationUrn;

        var rows = await requestRepository.GetAmendmentRequestsAsync(windowId, urn);
        var kept = rows
            .Where(r => r.Status == RequestStatus.ReadyToSubmit && selected.Contains(r.ReferenceNumber))
            .ToList();

        var alreadySubmitted = (await requestRepository.GetSubmittedPupilIdsAsync(windowId, urn)).ToHashSet();

        // Pupils appearing more than once across the kept selection.
        // A ReadyToSubmit draft always has a selected pupil, so PupilId is expected non-null here.
        // A row with a null PupilId (only plausible for legacy rows) can't be duplicate-matched and
        // is therefore treated as submittable — an accepted gap, not a silent data drop.
        var pupilCounts = kept
            .Where(r => r.PupilId is not null)
            .GroupBy(r => r.PupilId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var submittable = new List<BulkReviewItem>();
        var duplicates = new List<BulkReviewItem>();

        foreach (var row in kept)
        {
            var pupilId = row.PupilId;
            string? reason = null;
            if (pupilId is not null && alreadySubmitted.Contains(pupilId.Value))
                reason = AlreadySubmittedReason;
            else if (pupilId is not null && pupilCounts[pupilId.Value] > 1)
                reason = SelectedMoreThanOnceReason;

            var item = new BulkReviewItem
            {
                ReferenceNumber = row.ReferenceNumber,
                PupilName = PupilNameFormatter.Format(row.PupilFirstname, row.PupilSurname),
                RequestTypeDescription = row.RequestTypeDescription,
                DuplicateReason = reason
            };

            if (reason is null) submittable.Add(item);
            else duplicates.Add(item);
        }

        return new BulkReviewResult { Submittable = submittable, Duplicates = duplicates };
    }

    public async Task<BulkSubmissionResult> SubmitAsync(Guid windowId, IReadOnlyList<string> references)
    {
        // Re-run classification defensively so a tampered/stale POST cannot submit a duplicate.
        var review = await BuildReviewAsync(windowId, references);
        var toSubmit = review.Submittable.Select(i => i.ReferenceNumber).ToList();

        var submitted = new List<string>();
        var skipped = new List<string>();

        foreach (var reference in toSubmit)
        {
            var journey = await requestService.ResumeDraftAsync(windowId, reference);
            if (journey is null) { skipped.Add(reference); continue; }

            try
            {
                await requestService.SubmitRequestAsync(windowId, journey);
                submitted.Add(reference);
                await analytics.TrackSafeAsync(new RequestSubmittedEvent
                {
                    WhatToChange = journey.SelectedWhatToChange?.ToString() ?? "",
                    CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
                    ReferenceNumber = reference,
                });
            }
            catch (DuplicateRequestException)
            {
                skipped.Add(reference);
            }
        }

        if (submitted.Count > 0)
        {
            var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
            await requestNotificationService.NotifyBulkSubmissionConfirmedAsync(
                windowId, window.EndDate, submitted, EmailSubstitutions.From(window));
        }

        return new BulkSubmissionResult { Submitted = submitted, Skipped = skipped };
    }
}
