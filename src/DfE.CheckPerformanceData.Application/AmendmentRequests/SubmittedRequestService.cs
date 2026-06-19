using System.Globalization;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class SubmittedRequestService(
    IRequestStateBlobClient requestStateBlobClient,
    IQuestionFlowService flowService,
    IRequestRepository requestRepository,
    ICurrentUserService currentUserService) : ISubmittedRequestService
{
    public async Task<SubmittedRequestView?> GetAsync(Guid windowId, string referenceNumber)
    {
        var journey = await requestStateBlobClient.GetAsync(windowId, referenceNumber);
        if (journey?.SelectedWhatToChange is null || journey.CheckingWindow is null)
            return null;

        var config = await flowService.GetConfigAsync(
            journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
        if (config is null)
            return null;

        var pupilName = journey.SelectedPupil is { } p ? $"{p.Firstname} {p.Surname}".Trim() : string.Empty;

        var rows = new List<SubmittedRequestAnswerRow>();
        var files = new List<SubmittedRequestFileRow>();

        foreach (var pid in journey.QuestionHistory)
        {
            var page = flowService.GetPage(config, pid);
            if (page is null || page.Type == PageType.Content || page.Type == PageType.PupilSearch)
                continue;

            foreach (var q in page.Questions)
            {
                journey.QuestionAnswers.TryGetValue(q.Id, out var answer);
                if (q.Type == QuestionType.FileUpload)
                {
                    if (answer?.FileValues is { Count: > 0 } fileValues)
                        files.AddRange(fileValues.Select(f => new SubmittedRequestFileRow
                        {
                            OriginalFileName = f.OriginalFileName,
                            StoredFileName = f.StoredFileName,
                            FileSizeBytes = f.FileSizeBytes,
                            PageCount = f.PageCount
                        }));
                }
                else
                {
                    rows.Add(new SubmittedRequestAnswerRow
                    {
                        Title = JourneyTemplate.Resolve(q.SummaryTitle ?? q.Title, pupilName),
                        DisplayValue = DisplayValue(q, answer)
                    });
                }
            }
        }

        var (firstRecord, secondRecord) = BuildMergeDisplays(journey);

        return new SubmittedRequestView
        {
            WhatToChange = journey.SelectedWhatToChange.Value,
            PupilName = pupilName,
            FirstRecordDisplay = firstRecord,
            SecondRecordDisplay = secondRecord,
            Rows = rows,
            Files = files,
            ReferenceNumber = journey.ReferenceNumber ?? referenceNumber,
            SubmittedByEmail = journey.SubmittedByEmail,
            SubmittedAt = journey.SubmittedAt
        };
    }

    public async Task<ConfirmDataCorrectView?> GetConfirmDataCorrectAsync(Guid windowId, string referenceNumber)
    {
        var urn = long.Parse(currentUserService.OrganisationUrn);
        var row = await requestRepository.GetConfirmDataCorrectAsync(windowId, urn, referenceNumber);
        if (row is null || row.RequestType != RequestType.ConfirmCorrect)
            return null;

        return new ConfirmDataCorrectView
        {
            SubmittedByEmail = row.SubmittedByEmail,
            SubmittedAt = row.Submitted,
            ReferenceNumber = row.ReferenceNumber
        };
    }

    private static string DisplayValue(Question question, QuestionAnswer? answer) => question.Type switch
    {
        QuestionType.Date when answer?.DateValue is { } d =>
            $"{d.Day} {new DateTime(d.Year, d.Month, d.Day):MMMM yyyy}",
        QuestionType.Radio when answer?.TextValue is { } v =>
            question.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v,
        _ => answer?.TextValue ?? string.Empty
    };

    private static (string? first, string? second) BuildMergeDisplays(RequestState journey)
    {
        if (journey.MatchedPupil is not { } mp || journey.SelectedPupil is not { } sp)
            return (null, null);

        var dob = DateTime.TryParseExact(sp.DateOfBirth, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("d MMMM yyyy")
            : sp.DateOfBirth;
        var first = $"{sp.Firstname} {sp.Surname}, {dob}".Trim();
        var second = $"{mp.Cypmd_Id}, {mp.Firstname} {mp.Surname}".Trim();
        return (first, second);
    }
}
