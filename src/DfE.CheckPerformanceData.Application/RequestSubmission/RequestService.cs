using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class RequestService(
    IQuestionFlowService flowService,
    IRequestBlobClient requestBlobClient,
    IDraftBlobClient draftBlobClient,
    IRequestRepository requestRepository,
    ICurrentUserService currentUserService,
    INotifyService notifyService,
    IOptions<NotifySettings> notifySettings) : IRequestService
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
            WhatToChange = journey.SelectedWhatToChange.Value,
            Pupil = journey.SelectedPupil,
            MatchedPupil = journey.MatchedPupil,
            CheckingWindow = journey.CheckingWindow,
            Answers = journey.QuestionAnswers,
            History = journey.QuestionHistory
        };

        var document = BuildRequestDocument(context, config);
        await requestBlobClient.SaveRequestAsync(windowId, document);
        await requestRepository.UpsertAsync(BuildChangeRequestData(windowId, journey, RequestStatus.SubmittedUnCommitted, config));

        // Send submission notification email via GOV.UK Notify
        await notifyService.SendSubmissionNotificationAsync(
            currentUserService.Email,
            journey.ReferenceNumber ?? string.Empty,
            notifySettings.Value.DeadlineText,
            notifySettings.Value.SubmitOthersUrl);
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

    private RequestDocument BuildRequestDocument(JourneySubmissionContext context, QuestionFlowConfig config)
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
            ReferenceNumber = context.ReferenceNumber,
            SubmittedAt = DateTime.UtcNow,
            SubmittedBy = new UserDetails
            {
                UserId = currentUserService.UserId,
                DisplayName = currentUserService.DisplayName
            },
            CheckingWindowId = context.WindowId,
            CheckingWindowType = context.CheckingWindow.CheckingWindowType.ToString(),
            WhatToChange = context.WhatToChange.ToString(),
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
                Upn = pupil.Upn
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
                Upn = mp.Upn
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

        return new AnswerRecord
        {
            QuestionId = question.Id,
            QuestionTitle = title,
            Type = question.Type.ToString(),
            Value = value
        };
    }
}
