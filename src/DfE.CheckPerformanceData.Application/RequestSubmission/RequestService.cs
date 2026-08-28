using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class RequestService(
    IQuestionFlowService flowService,
    IRequestStateBlobClient requestStateBlobClient,
    IRequestRepository requestRepository,
    ICurrentUserService currentUserService,
    ILogger<RequestService> logger,
    IQueueService queueService,
    IRequestNotificationService requestNotificationService,
    ICheckYourPupilDataService checkYourPupilDataService) : IRequestService
{
    private long OrganisationUrnLong => long.Parse(currentUserService.OrganisationUrn);

    private string ExtractCurrentReasonType(RequestState journey, QuestionFlowConfig? config)
    {
        if (config is null)
            return journey.SelectedWhatToChange?.ToString() ?? string.Empty;

        var detail = flowService.ResolveRequestType(config, journey);
        return string.IsNullOrEmpty(detail)
            ? journey.SelectedWhatToChange?.ToString() ?? string.Empty
            : detail;
    }

    public async Task<DuplicateCheckResult> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn)
    {
        var userId = Guid.Parse(currentUserService.UserId);
        return await requestRepository.CheckForConflictAsync(windowId, pupilId, organisationUrn, string.Empty, userId);
    }

    public async Task SubmitRequestAsync(Guid windowId, RequestState journey)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null || journey.SelectedPupil is null)
            throw new InvalidOperationException("Session state is incomplete for request submission.");

        var config = await flowService.GetConfigAsync(journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);

        var urnLong = OrganisationUrnLong;
        var refNum = journey.ReferenceNumber ?? string.Empty;
        var userId = Guid.Parse(currentUserService.UserId);
        var conflict = await requestRepository.CheckForConflictAsync(windowId, journey.SelectedPupil.Id, urnLong, refNum, userId);
        if (conflict is DuplicateCheckResult.SelfSubmitted { ConflictingReasonType: var conflictingReasonType, ConflictingRequestCategory: var selfCategory, ConflictingUserName: var selfUserName })
        {
            var currentReasonType = ExtractCurrentReasonType(journey, config);
            var reasonsMatch = string.Equals(currentReasonType, conflictingReasonType, StringComparison.OrdinalIgnoreCase);
            throw new DuplicateRequestException(ConflictType.SelfSubmitted, conflictingReasonType, selfCategory, selfUserName, reasonsMatch);
        }
        if (conflict is DuplicateCheckResult.OtherSubmitted { ConflictingReasonType: var otherReasonType, ConflictingRequestCategory: var otherCategory, ConflictingUserName: var otherUserName })
        {
            var currentReasonType = ExtractCurrentReasonType(journey, config);
            var reasonsMatch = string.Equals(currentReasonType, otherReasonType, StringComparison.OrdinalIgnoreCase);
            throw new DuplicateRequestException(ConflictType.OtherSubmitted, otherReasonType, otherCategory, otherUserName, reasonsMatch);
        }

        if (config is null)
            throw new InvalidOperationException(
                $"No question flow config found for {journey.SelectedWhatToChange}/{journey.CheckingWindow.CheckingWindowType}.");

        var context = new JourneySubmissionContext
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber ?? string.Empty,
            WhatToChange = BuildWhatToChangeValue(journey, config),
            Pupil = journey.SelectedPupil,
            MatchedPupil = journey.MatchedPupil,
            CheckingWindow = journey.CheckingWindow,
            Answers = journey.QuestionAnswers,
            History = journey.QuestionHistory
        };

        // Upsert first: the document carries the ChangeRequest row's Id so the
        // rules engine worker can write its decision back to that row.
        var changeRequestId = await requestRepository.UpsertAsync(
            BuildChangeRequestData(windowId, journey, RequestStatus.SubmittedUnCommitted, config));

        // PARKED AB#297310: an Add-pupil request has no rules-engine outcomes (ticket B2) — its
        // downstream is the LDS egress (LDS_CYPMD_Data specification v2.4), a separate story. So
        // no enqueue: the ChangeRequests row + the journey blob saved below are the record that
        // egress will read. When the egress story lands, its dispatch goes THERE, not here — and
        // the matching exclusion in AdminRequestsService.ProcessCloseWindowEvent comes out with it.
        if (journey.SelectedWhatToChange != WhatToChange.Add)
        {
            var document = BuildRequestDocument(context, config, changeRequestId);

            // Enqueue onto the Postgres rules-engine queue; the worker's RulesConsumer
            // picks it up, evaluates it and writes the decision back to the row.
            await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, document);
        }

        // Persist the stamped journey so the read-only submitted-request view can
        // rebuild its summary (and "Submitted by" section) from the journey alone —
        // the enqueued RequestDocument is bound for the queue and not retained.
        await requestStateBlobClient.SaveAsync(windowId, journey.ReferenceNumber ?? string.Empty, journey);
    }

    public async Task<string> SubmitResultsEnquiryAsync(
        Guid windowId, RequestState journey, CancellationToken ct = default)
    {
        if (journey.SelectedWhatToChange
            is not (WhatToChange.IncorrectGrade or WhatToChange.MissingQualification))
            throw new InvalidOperationException(
                $"SubmitResultsEnquiryAsync is the results-enquiry path; got {journey.SelectedWhatToChange}. " +
                "Routing an amendment through here would store the wrong RequestType and skip the rules engine.");

        // Each enquiry kind has its own resolved subject: an incorrect grade is about a held
        // result, a missing qualification about a QualList entry. The other is legitimately null.
        var hasSubject = journey.SelectedWhatToChange == WhatToChange.IncorrectGrade
            ? journey.SelectedResult is not null
            : journey.SelectedQualification is not null;

        if (journey.CheckingWindow is null || journey.SelectedPupil is null || !hasSubject
            || string.IsNullOrWhiteSpace(journey.ReferenceNumber))
            throw new InvalidOperationException("Session state is incomplete for results-enquiry submission.");

        // The row first: it is the record of truth, and a journey blob with no row would be invisible
        // to every admin view.
        await requestRepository.UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber,
            OrganisationUrn = OrganisationUrnLong,
            PupilId = journey.SelectedPupil.Id,
            PupilUpn = journey.SelectedPupil.Identifier,
            PupilFirstname = journey.SelectedPupil.Firstname,
            PupilSurname = journey.SelectedPupil.Surname,
            // Stored as UTC and converted to London time at display. The column is
            // `timestamp without time zone`, so the value carries an Unspecified kind
            // (Npgsql rejects a Utc kind here); the instant it holds is UTC.
            Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.Parse(currentUserService.UserId),
            SubmittedByName = currentUserService.DisplayName,
            SubmittedByEmail = currentUserService.Email,
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = RequestType.ResultsEnquiry,
            RequestTypeDescription = journey.SelectedWhatToChange == WhatToChange.IncorrectGrade
                ? "Results enquiry - Incorrect grade"
                : "Results enquiry - Missing qualification",
            AmendmentType = journey.SelectedWhatToChange.Value
        });

        // The journey JSON is the enquiry's full record — it carries the selected result and every
        // answer — so the separate Zendesk story can build its ticket from this without a new schema.
        await requestStateBlobClient.SaveAsync(windowId, journey.ReferenceNumber, journey);

        // PARKED AB#296648: no enqueue. Enquiries are destined for Zendesk, but the dispatch is a
        // separate ticket. When it lands, the enqueue goes HERE and nowhere else — and the exclusion
        // in AdminRequestsService.ProcessCloseWindowEvent comes out at the same time.

        return journey.ReferenceNumber;
    }

    public async Task ConfirmRequestAsync(Guid windowId, RequestState journey)
    {
        await SubmitRequestAsync(windowId, journey);
        await requestNotificationService.NotifySubmissionConfirmedAsync(
            windowId, journey.CheckingWindow!.EndDate, journey.ReferenceNumber ?? string.Empty,
            EmailSubstitutions.From(journey.CheckingWindow));
    }

    public async Task ConfirmDataCorrectAsync(
        Guid windowId, string referenceNumber, DateTime endDate, EmailSubstitutions substitutions)
    {
        await requestRepository.UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = OrganisationUrnLong,
            // Stored as UTC and converted to London time at display. The column is
            // `timestamp without time zone`, so the value carries an Unspecified kind
            // (Npgsql rejects a Utc kind here); the instant it holds is UTC.
            Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.Parse(currentUserService.UserId),
            SubmittedByName = currentUserService.DisplayName,
            SubmittedByEmail = currentUserService.Email,
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = RequestType.ConfirmCorrect,
            RequestTypeDescription = "Confirm Pupil Data Declaration"
        });

        await requestNotificationService.NotifyDataCheckConfirmedAsync(
            endDate, referenceNumber, substitutions);
    }

    public async Task SaveDraftAsync(Guid windowId, RequestState journey, RequestStatus status)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null || journey.SelectedPupil is null
            || journey.ReferenceNumber is null)
            throw new InvalidOperationException("Session state is incomplete for draft submission.");

        await requestStateBlobClient.SaveAsync(windowId, journey.ReferenceNumber, journey);
        var draftConfig = await flowService.GetConfigAsync(journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
        await requestRepository.UpsertAsync(BuildChangeRequestData(windowId, journey, status, draftConfig));
    }

    public async Task<RequestState?> ResumeDraftAsync(Guid windowId, string referenceNumber)
    {
        // Org-scoping gate: the draft blob is keyed only by windowId + referenceNumber, so
        // verify the caller's organisation owns a ChangeRequests row for this reference before
        // reading the blob. Otherwise another school's draft PII would be disclosed and loaded
        // into the attacker's session. Fail closed on a missing row.
        var row = await requestRepository.GetAmendmentRequestAsync(windowId, OrganisationUrnLong, referenceNumber);
        if (row is null)
            return null;

        return await requestStateBlobClient.GetAsync(windowId, referenceNumber);
    }

    public async Task<RequestDeletionResult> DeleteAsync(Guid windowId, string referenceNumber)
    {
        var urn = OrganisationUrnLong;
        var row = await requestRepository.GetAmendmentRequestAsync(windowId, urn, referenceNumber);
        var pupilName = row is null ? string.Empty : $"{row.PupilFirstname} {row.PupilSurname}".Trim();

        // Drafts have never been submitted, so they are removed entirely (row + journey blob).
        // Submitted requests are kept for audit and only marked Withdrawn.
        if (row?.Status is RequestStatus.InProgress or RequestStatus.ReadyToSubmit)
        {
            await requestRepository.DeleteAsync(windowId, urn, referenceNumber);
            await requestStateBlobClient.DeleteAsync(windowId, referenceNumber);
            return new RequestDeletionResult(WasHardDeleted: true, pupilName, row.RequestType);
        }

        await requestRepository.WithdrawAsync(windowId, urn, referenceNumber, currentUserService.Email, DateTime.UtcNow);

        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var deadline = window.EndDate;

        if (row?.RequestType == RequestType.Amendment)
        {
            await requestNotificationService.NotifyAmendmentWithdrawnAsync(
                referenceNumber, deadline, EmailSubstitutions.From(window));
        }
        else if (row?.RequestType == RequestType.ConfirmCorrect)
        {
            await requestNotificationService.NotifyDataCheckWithdrawnAsync(
                referenceNumber, deadline, EmailSubstitutions.From(window));
        }
        else
        {
            logger.LogWarning("Unexpected request type {RequestType} for ref {RefNumber} - withdrawal notification skipped", row?.RequestType , referenceNumber);
        }

        return new RequestDeletionResult(WasHardDeleted: false, pupilName, row?.RequestType);
    }

    private string BuildRequestTypeDescription(RequestState journey, QuestionFlowConfig? config)
    {
        var prefix = journey.SelectedWhatToChange!.Value.ToString();
        if (config is null) return prefix;

        var detail = flowService.ResolveRequestType(config, journey);
        return string.IsNullOrEmpty(detail) ? prefix : $"{prefix} - {detail}";
    }

    // The rules-engine contract: like BuildRequestType but using the stable option
    // *value* rather than the display label, so UI copy changes cannot break the
    // engine's WhatToChangeToOutcomeKey routing.
    private string BuildWhatToChangeValue(RequestState journey, QuestionFlowConfig config)
    {
        var prefix = journey.SelectedWhatToChange!.Value.ToString();
        var detail = flowService.ResolveRequestTypeValue(config, journey);
        return string.IsNullOrEmpty(detail) ? prefix : $"{prefix} - {detail}";
    }

    private ChangeRequestData BuildChangeRequestData(Guid windowId, RequestState journey, RequestStatus status, QuestionFlowConfig? config) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber!,
            OrganisationUrn = OrganisationUrnLong,
            PupilId = journey.SelectedPupil!.Id,
            PupilUpn = journey.SelectedPupil.Identifier,
            PupilFirstname = journey.SelectedPupil.Firstname,
            PupilSurname = journey.SelectedPupil.Surname,
            // Stored as UTC and converted to London time at display. The column is
            // `timestamp without time zone`, so the value carries an Unspecified kind
            // (Npgsql rejects a Utc kind here); the instant it holds is UTC.
            Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.Parse(currentUserService.UserId),
            SubmittedByName = currentUserService.DisplayName,
            SubmittedByEmail = currentUserService.Email,
            Status = status,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = BuildRequestTypeDescription(journey, config),
            AmendmentType = journey.SelectedWhatToChange
        };

    private RequestDocument BuildRequestDocument(JourneySubmissionContext context, QuestionFlowConfig config, Guid changeRequestId)
    {
        var pupil = context.Pupil;
        var pupilName = $"{pupil.Firstname} {pupil.Surname}".Trim();

        var answers = context.History
            .SelectMany(pid =>
            {
                var page = config.Pages.FirstOrDefault(p => p.Id == pid);
                if (page is null || page.Type == PageType.Content || page.Type == PageType.PupilSearch) return Enumerable.Empty<AnswerRecord>();
                return page.Questions.Select(q =>
                {
                    context.Answers.TryGetValue(q.Id, out var ans);
                    return BuildAnswerRecord(q, ans, pupilName);
                });
            })
            .ToList();

        return new RequestDocument
        {
            ChangeRequestId = changeRequestId,
            ReferenceNumber = context.ReferenceNumber,
            SubmittedAt = DateTime.UtcNow,
            SubmittedBy = new UserDetails
            {
                UserId = currentUserService.UserId,
                DisplayName = currentUserService.DisplayName
            },
            CheckingWindowId = context.WindowId,
            CheckingWindowType = context.CheckingWindow.CheckingWindowType.ToString(),
            RequestTypeCode = context.WhatToChange,
            School = new SchoolDetails
            {
                Urn = currentUserService.OrganisationUrn,
                Name = currentUserService.OrganisationName,
                Laestab = string.IsNullOrEmpty(pupil.Laestab)
                    ? currentUserService.OrganisationLaestab
                    : pupil.Laestab
            },
            Pupil = new PupilDetails
            {
                Id = pupil.Id.ToString(),
                CypmdId = pupil.Cypmd_Id,
                Firstname = pupil.Firstname,
                Surname = pupil.Surname,
                DateOfBirth = pupil.DateOfBirth,
                Sex = pupil.Sex,
                Age = pupil.Age,
                Upn = pupil.Identifier,
                Pincl = pupil.Pincl,
                MatchRef = pupil.MatchRef,
                EntryDate = pupil.EntryDate
            },
            MatchedPupil = context.MatchedPupil is { } mp ? new PupilDetails
            {
                Id = mp.Id.ToString(),
                CypmdId = mp.Cypmd_Id,
                Firstname = mp.Firstname,
                Surname = mp.Surname,
                DateOfBirth = mp.DateOfBirth,
                Sex = mp.Sex,
                Age = mp.Age,
                Upn = mp.Identifier,
                Pincl = mp.Pincl,
                MatchRef = mp.MatchRef,
                EntryDate = mp.EntryDate
            } : null,
            Answers = answers
        };
    }

    private static AnswerRecord BuildAnswerRecord(Question question, QuestionAnswer? answer, string pupilName)
    {
        var title = JourneyTemplate.Resolve(question.Title, pupilName);

        if (question.Type == QuestionType.FileUpload)
        {
            return new AnswerRecord
            {
                QuestionId = question.Id,
                QuestionTitle = title,
                Type = "FileUpload",
                Files = answer?.FileValues?.Select(f => new FileRecord
                {
                    OriginalFileName = f.OriginalFileName,
                    StoredFileName = f.StoredFileName,
                    PageCount = f.PageCount,
                    FileSizeBytes = f.FileSizeBytes
                }).ToList()
            };
        }

        var value = question.Type switch
        {
            QuestionType.Radio when answer?.TextValue is { } v =>
                question.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v,
            QuestionType.Date when answer?.DateValue is { } d =>
                $"{d.Day:D2}/{d.Month:D2}/{d.Year}",
            _ => answer?.TextValue
        };

        var rawValue = question.Type switch
        {
            QuestionType.Date when answer?.DateValue is { } d =>
                $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}",
            QuestionType.Autocomplete => answer?.CodeValue ?? answer?.TextValue,
            _ => answer?.TextValue
        };

        return new AnswerRecord
        {
            QuestionId = question.Id,
            QuestionTitle = title,
            Type = question.Type.ToString(),
            Value = value,
            RawValue = rawValue
        };
    }
}
