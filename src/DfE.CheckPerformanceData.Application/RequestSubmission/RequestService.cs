using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class RequestService(
    IQuestionFlowService flowService,
    IDraftBlobClient draftBlobClient,
    IRequestRepository requestRepository,
    INotifyService notifyService,
    IOptions<NotifySettings> notifySettings,
    IDfESignInApiClient dfESignInApiClient,
    ICurrentUserService currentUserService,
    IRequestQueueClient requestQueueClient,
    IRequestBlobClient requestBlobClient,
    RequestSubmissionOptions submissionOptions) : IRequestService
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

        // TEMPORARY: the rules-engine queue path is paused. When the switch is on the
        // document is written to blob storage instead of being enqueued. See
        // RequestSubmissionOptions.
        if (submissionOptions.WriteToBlobInsteadOfQueue)
            await requestBlobClient.SaveRequestAsync(windowId, document);
        else
            await requestQueueClient.EnqueueRequestAsync(document);

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentUserService.Email };

        var userRoles = await dfESignInApiClient.GetUserRolesAsync(currentUserService.OrganisationId, currentUserService.UserId);
        // var approvers = await dfESignInApiClient.GetApproversAsync();//GetOrganisationApproversAsync(currentUserService.OrganisationId);
        // if (approvers?.Users != null)
        //     foreach (var approver in approvers.Users) recipients.Add(approver.Email);

        var orgUsers = await dfESignInApiClient.GetOrganisationUsersAsync(currentUserService.Ukprn); //currentUserService.OrganisationUrn);
        if (orgUsers?.Users != null)
            foreach (var user in orgUsers.Users)
                recipients.Add(user.Email);
        
        foreach (var email in recipients)
        {
            await notifyService.SendSubmissionNotificationAsync(
                email,
                refNum,
                notifySettings.Value.DeadlineText,
                notifySettings.Value.SubmitOthersUrl);
        }
    }

    public async Task ConfirmDataCorrectAsync(Guid windowId, string referenceNumber)
    {
        await requestRepository.UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = OrganisationUrnLong,
            Timestamp = DateTime.UtcNow,
            SubmittedById = Guid.Parse(currentUserService.UserId),
            SubmittedByName = currentUserService.DisplayName,
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = "Confirm Pupil Data Declaration"
        });
    }

    public async Task SaveDraftAsync(Guid windowId, RequestState journey, RequestStatus status)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null || journey.SelectedPupil is null
            || journey.ReferenceNumber is null)
            throw new InvalidOperationException("Session state is incomplete for draft submission.");

        await draftBlobClient.SaveDraftAsync(windowId, journey.ReferenceNumber, journey);
        var draftConfig = await flowService.GetConfigAsync(journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
        await requestRepository.UpsertAsync(BuildChangeRequestData(windowId, journey, status, draftConfig));
    }

    public Task<RequestState?> ResumeDraftAsync(Guid windowId, string referenceNumber) =>
        draftBlobClient.GetDraftAsync(windowId, referenceNumber);

    private string BuildRequestType(RequestState journey, QuestionFlowConfig? config)
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
            Timestamp = DateTime.UtcNow,
            SubmittedById = Guid.Parse(currentUserService.UserId),
            SubmittedByName = currentUserService.DisplayName,
            Status = status,
            RequestType = BuildRequestType(journey, config)
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
