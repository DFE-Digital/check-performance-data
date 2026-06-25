using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class RequestService(
    IQuestionFlowService flowService,
    IRequestStateBlobClient requestStateBlobClient,
    IRequestRepository requestRepository,
    INotifyService notifyService,
    IDfESignInApiClient dfESignInApiClient,
    ICurrentUserService currentUserService,
    IRequestBlobClient requestBlobClient,
    ILogger<RequestService> logger,
    IEmailLinkGenerator emailLinkGenerator,
    IQueueService queueService) : IRequestService
{
    private long OrganisationUrnLong => long.Parse(currentUserService.OrganisationUrn);

    public async Task ConfirmRequestAsync(Guid windowId, RequestState journey)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null || journey.SelectedPupil is null)
            throw new InvalidOperationException("Session state is incomplete for request submission.");

        var urnLong = OrganisationUrnLong;
        var refNum = journey.ReferenceNumber ?? string.Empty;
        if (await requestRepository.HasConflictingRequestAsync(windowId, journey.SelectedPupil.Upn, urnLong, refNum))
            throw new DuplicateRequestException();

        var config = await flowService.GetConfigAsync(journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
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
        var document = BuildRequestDocument(context, config, changeRequestId);

        // Enqueue onto the Postgres rules-engine queue; the worker's RulesConsumer
        // picks it up, evaluates it and writes the decision back to the row.
        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, document);

        var recipients = await BuildNotificationRecipients();

        logger.LogInformation(
            "Sending Submission Notification emails for ref {RefNumber} to {RecipientCount} recipient(s) ({Recipients})",
            refNum, recipients.Count, string.Join(", ", recipients));

        var linkUrl = emailLinkGenerator.GenerateLink("WhatToChange", "Index", new { windowId }, "SubmissionNotification");

        var deadline = $"{journey.CheckingWindow.EndDate.ToString("htt").ToLower()} on {journey.CheckingWindow.EndDate:dddd d MMMM yyyy}";

        await notifyService.SendNotificationsAsync(
            refNum,
            deadline,
            recipients,
            NotificationType.SubmissionConfirmed,
            linkUrl);
    

        // Persist the stamped journey so the read-only submitted-request view can
        // rebuild its summary (and "Submitted by" section) from the journey alone —
        // the enqueued RequestDocument is bound for the queue and not retained.
        await requestStateBlobClient.SaveAsync(windowId, journey.ReferenceNumber ?? string.Empty, journey);
    }

    public async Task ConfirmDataCorrectAsync(Guid windowId, string referenceNumber, string deadline)
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

        var recipients = await BuildNotificationRecipients();

        logger.LogInformation(
            "Sending Pupil Data Check Confirm email for ref {RefNumber} to {RecipientCount} recipient(s) ({Recipients})",
            referenceNumber, recipients.Count, string.Join(", ", recipients));

        await notifyService.SendNotificationsAsync(
            referenceNumber,
            deadline,
            recipients,
            NotificationType.DataCheckConfirmed);
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

    public Task<RequestState?> ResumeDraftAsync(Guid windowId, string referenceNumber) =>
        requestStateBlobClient.GetAsync(windowId, referenceNumber);

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
            return new RequestDeletionResult(WasHardDeleted: true, pupilName);
        }

        await requestRepository.WithdrawAsync(windowId, urn, referenceNumber);

        HashSet<string> recipients;
        NotificationType notificationType;
        string logTemplate;
        object[] logArgs;

        if (row?.RequestType == RequestType.Amendment)
        {
            recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentUserService.Email };
            notificationType = NotificationType.AmendmentWithdrawn;
            logTemplate = "Sending Withdrawal notification email for ref {RefNumber} to session user ({Email})";
            logArgs = [referenceNumber, currentUserService.Email];
        }
        else if (row?.RequestType == RequestType.ConfirmCorrect)
        {
            recipients = await BuildNotificationRecipients();
            notificationType = NotificationType.DataCheckWithdrawn;
            logTemplate = "Sending Withdrawal notification email for ref {RefNumber} to {RecipientCount} recipient(s) ({Recipients})";
            logArgs = [referenceNumber, recipients.Count, string.Join(", ", recipients)];
        }
        else
        {
            logger.LogWarning("Unexpected request type {RequestType} for ref {RefNumber} - withdrawal notification skipped", row.RequestType, referenceNumber);
            return new RequestDeletionResult(WasHardDeleted: false, pupilName);
        }

        logger.LogInformation(logTemplate, logArgs);
        await notifyService.SendNotificationsAsync(referenceNumber, string.Empty, recipients, notificationType);

        return new RequestDeletionResult(WasHardDeleted: false, pupilName);
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
            PupilUpn = journey.SelectedPupil!.Upn,
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
            RequestTypeDescription = BuildRequestTypeDescription(journey, config)
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
                Name = currentUserService.OrganisationName
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
                Upn = pupil.Upn,
                Pincl = pupil.Pincl
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
                Upn = mp.Upn,
                Pincl = mp.Pincl
            } : null,
            Answers = answers
        };
    }

    private async Task<HashSet<string>> BuildNotificationRecipients()
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentUserService.Email };

        var orgUsers = await dfESignInApiClient.GetOrganisationUsersAsync(currentUserService.Ukprn);
        if (orgUsers?.Users != null)
            foreach (var user in orgUsers.Users)
                recipients.Add(user.Email);

        return recipients;
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
