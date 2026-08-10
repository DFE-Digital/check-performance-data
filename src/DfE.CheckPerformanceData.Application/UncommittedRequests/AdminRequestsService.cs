using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

public sealed class AdminRequestsService(
    IUncommittedRequestsRepository repository,
    IRequestStateBlobClient requestStateBlobClient,
    IQuestionFlowService flowService,
    IQueueService queueService,
    TimeProvider timeProvider) : IAdminRequestsService
{
    public Task<IReadOnlyList<UncommittedRequestRow>> GetAsync(CancellationToken cancellationToken) =>
        repository.GetAllAsync(cancellationToken);

    public async Task<int> ProcessCloseWindowEvent(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetLocalNow().DateTime;
        var rows = await repository.GetRequestsForOpenWindowsAsync(now, cancellationToken);

        var enqueued = 0;
        foreach (var row in rows)
        {
            var journey = await requestStateBlobClient.GetAsync(row.WindowId, row.ReferenceNumber);
            if (journey?.SelectedWhatToChange is null || journey.CheckingWindow is null || journey.SelectedPupil is null)
                continue;

            var config = await flowService.GetConfigAsync(
                journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
            if (config is null)
                continue;

            var document = BuildDocument(row, journey, config);
            await queueService.EnqueueAsync(QueueOptions.ZendeskQueue, document, cancellationToken);

            // Committed once the document is on the queue: SubmittedUnCommitted -> SubmittedCommitted.
            await repository.SetStatusAsync(row.ChangeRequestId, RequestStatus.SubmittedCommitted, cancellationToken);
            enqueued++;
        }

        // Drafts in the open window(s) were never submitted: InProgress / ReadyToSubmit -> NotSubmitted.
        await repository.MarkDraftsNotSubmittedForOpenWindowsAsync(now, cancellationToken);

        return enqueued;
    }

    // Rebuilds a RequestDocument from the persisted ChangeRequests row + RequestState blob.
    // Unlike RequestService.ConfirmRequestAsync, the submitter/school come from the stored
    // row (this runs as an admin, not the original submitter). School.Name is not persisted
    // anywhere, so it is left blank — acceptable for this throwaway test path.
    private RequestDocument BuildDocument(ReplayRequestRow row, RequestState journey, QuestionFlowConfig config)
    {
        var pupil = journey.SelectedPupil!;
        var pupilName = $"{pupil.Firstname} {pupil.Surname}".Trim();

        var requestTypeDetail = flowService.ResolveRequestTypeValue(config, journey);
        var requestTypeCode = string.IsNullOrEmpty(requestTypeDetail)
            ? journey.SelectedWhatToChange!.Value.ToString()
            : $"{journey.SelectedWhatToChange!.Value} - {requestTypeDetail}";

        var answers = journey.QuestionHistory
            .SelectMany(pid =>
            {
                var page = config.Pages.FirstOrDefault(p => p.Id == pid);
                if (page is null || page.Type == PageType.Content || page.Type == PageType.PupilSearch)
                    return Enumerable.Empty<AnswerRecord>();
                return page.Questions.Select(q =>
                {
                    journey.QuestionAnswers.TryGetValue(q.Id, out var ans);
                    return BuildAnswerRecord(q, ans, pupilName);
                });
            })
            .ToList();

        return new RequestDocument
        {
            ChangeRequestId = row.ChangeRequestId,
            ReferenceNumber = row.ReferenceNumber,
            SubmittedAt = DateTime.UtcNow,
            SubmittedBy = new UserDetails
            {
                UserId = row.SubmittedById.ToString(),
                DisplayName = row.SubmittedByName
            },
            CheckingWindowId = row.WindowId,
            CheckingWindowType = journey.CheckingWindow!.CheckingWindowType.ToString(),
            RequestTypeCode = requestTypeCode,
            School = new SchoolDetails
            {
                Urn = row.OrganisationUrn.ToString(),
                Name = string.Empty,
                Laestab = pupil.Laestab
            },
            Pupil = ToPupilDetails(pupil),
            MatchedPupil = journey.MatchedPupil is { } mp ? ToPupilDetails(mp) : null,
            Answers = answers
        };
    }

    private static PupilDetails ToPupilDetails(CheckYourPupilData.PupilDto p) => new()
    {
        Id = p.Id.ToString(),
        CypmdId = p.Cypmd_Id,
        Firstname = p.Firstname,
        Surname = p.Surname,
        DateOfBirth = p.DateOfBirth,
        Sex = p.Sex,
        Age = p.Age,
        Upn = p.Identifier,
        Pincl = p.Pincl,
        MatchRef = p.MatchRef,
        EntryDate = p.EntryDate
    };

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
