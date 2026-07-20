using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyViewModelBuilder(
    IQuestionFlowService flowService,
    IJourneyValidationService journeyService,
    IOptionVisibilityService optionVisibilityService,
    ICurrentUserService currentUserService) : IJourneyViewModelBuilder
{
    public SummaryViewModel BuildSummaryVm(
        Guid windowId, RequestState journey, QuestionFlowConfig config,
        string? conflictError = null, bool fromBulk = false, bool fromEdit = false)
    {
        var pupilName = GetPupilName(journey);
        var rows = new List<SummaryRow>();
        var fileRows = new List<SummaryFileRow>();

        foreach (var pid in journey.QuestionHistory)
        {
            var p = flowService.GetPage(config, pid);
            if (p is null || p.Type == PageType.Content || p.Type == PageType.PupilSearch) continue;
            foreach (var q in p.Questions)
            {
                journey.QuestionAnswers.TryGetValue(q.Id, out var a);
                if (q.Type == QuestionType.FileUpload)
                {
                    if (a?.FileValues is { Count: > 0 } files)
                        fileRows.AddRange(files.Select(f =>
                            new SummaryFileRow(p, f.OriginalFileName, f.FileSizeBytes, f.PageCount, f.StoredFileName)));
                }
                else
                {
                    rows.Add(new SummaryRow(p, q, a, JourneyTemplate.Resolve(q.SummaryTitle ?? q.Title, pupilName)));
                }
            }
        }

        var backPageId = journey.QuestionHistory.Last();
        var backPage = flowService.GetPage(config, backPageId);

        var primaryPupilPage = config.Pages.FirstOrDefault(
            p => p.Type == PageType.PupilSearch && p.PupilKey == JourneyPage.PrimaryKey);
        var matchPupilPage = config.Pages.FirstOrDefault(
            p => p.Type == PageType.PupilSearch && p.PupilKey == JourneyPage.MatchKey);

        string? firstRecordDisplay = null;
        string? secondRecordDisplay = null;
        if (journey.MatchedPupil is { } mp && journey.SelectedPupil is { } sp)
        {
            var dob = DateTime.TryParseExact(sp.DateOfBirth, "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d)
                ? d.ToString("d MMMM yyyy")
                : sp.DateOfBirth;
            firstRecordDisplay = $"{sp.Firstname} {sp.Surname}, {dob}".Trim();
            secondRecordDisplay = $"{mp.Cypmd_Id}, {mp.Firstname} {mp.Surname}".Trim();
        }

        return new SummaryViewModel
        {
            WindowId = windowId,
            WhatToChange = journey.SelectedWhatToChange!.Value,
            PupilName = pupilName,
            Rows = rows,
            FileRows = fileRows,
            BackPageId = backPageId,
            MaxEvidencePages = journeyService.MaxEvidencePages,
            ConflictError = conflictError,
            FromBulk = fromBulk,
            FromEdit = fromEdit,
            PrimaryPupilPageId = primaryPupilPage?.Id,
            FirstRecordDisplay = firstRecordDisplay,
            SecondRecordDisplay = secondRecordDisplay,
            MatchedPupilPageId = matchPupilPage?.Id,
            BackPageIsPupilSearch = backPage?.Type == PageType.PupilSearch
        };
    }

    public PageViewModel BuildPageVm(
        Guid windowId,
        JourneyPage page,
        Dictionary<string, QuestionAnswer> answers,
        RequestState journey,
        bool fromSummary,
        ModelStateDictionary modelState,
        QuestionFlowConfig? config = null,
        string? uploadError = null,
        string? atLeastOneError = null)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(page.Id);
        var backPageId = historyIndex switch
        {
            -1 => journey.QuestionHistory.LastOrDefault(),
            0  => null,
            _  => journey.QuestionHistory[historyIndex - 1]
        };
        var backPageIsPupilSearch = backPageId is not null && config is not null
            && flowService.GetPage(config, backPageId)?.Type == PageType.PupilSearch;

        var pupilName = GetPupilName(journey);
        var isSingleQuestion = page.Questions.Count == 1;

        string? contentKey = null;
        if (page.Type is PageType.Content or PageType.EvidenceUpload && config is not null)
            contentKey = flowService.BuildContentKey(windowId, page, answers, journey, config);

        var conditionContext = BuildConditionContext(journey);

        var questionModels = page.Questions.Select(q =>
        {
            var error = modelState.TryGetValue(q.Id, out var entry)
                ? entry.Errors.FirstOrDefault()?.ErrorMessage
                : null;
            return new QuestionPartialModel
            {
                WindowId = windowId,
                PageId = page.Id,
                Question = q,
                ExistingAnswer = answers.TryGetValue(q.Id, out var a) ? a : null,
                FromSummary = fromSummary,
                IsPageHeading = isSingleQuestion && string.IsNullOrEmpty(page.Title),
                MaxEvidencePages = journeyService.MaxEvidencePages,
                Error = error,
                UploadError = uploadError,
                ResolvedTitle = JourneyTemplate.Resolve(q.Title, pupilName) + (q.Optional ? " (Optional)" : ""),
                VisibleOptions = q.Type == QuestionType.Radio
                    ? optionVisibilityService.GetVisibleOptions(q, conditionContext)
                    : q.Options ?? []
            };
        }).ToList();

        return new PageViewModel
        {
            WindowId = windowId,
            Page = page,
            Answers = answers,
            BackPageId = backPageId,
            BackPageIsPupilSearch = backPageIsPupilSearch,
            FromSummary = fromSummary,
            PupilName = pupilName,
            ContentKey = contentKey,
            UploadError = uploadError,
            AtLeastOneError = atLeastOneError,
            QuestionModels = questionModels
        };
    }

    public PupilSearchViewModel BuildPupilSearchVm(
        Guid windowId, string pageId, JourneyPage page, RequestState journey, QuestionFlowConfig config)
    {
        var pupilName = GetPupilName(journey);
        var title = page.Title is not null ? JourneyTemplate.Resolve(page.Title, pupilName) : string.Empty;
        Guid? excludeId = null;
        if (page.PupilKey == JourneyPage.MatchKey && Guid.TryParse(journey.SelectedPupilId, out var pid))
            excludeId = pid;

        var (existingId, existingLabel) = page.PupilKey == JourneyPage.MatchKey
            ? (journey.MatchedPupilId, journey.MatchedPupilLabel)
            : (journey.SelectedPupilId, journey.SelectedPupilLabel);

        string? backPageId = null;
        bool backPageIsPupilSearch = false;
        var historyIndex = journey.QuestionHistory.IndexOf(pageId);
        var backEntry = historyIndex > 0
            ? journey.QuestionHistory[historyIndex - 1]
            : historyIndex < 0 && journey.QuestionHistory.Count > 0
                ? journey.QuestionHistory[^1]
                : null;
        if (backEntry is not null)
        {
            backPageId = backEntry;
            backPageIsPupilSearch = flowService.GetPage(config, backEntry)?.Type == PageType.PupilSearch;
        }

        return new PupilSearchViewModel
        {
            WindowId = windowId,
            PageId = pageId,
            Title = title,
            Filter = page.PupilFilter ?? PupilFilter.Included,
            ExcludePupilId = excludeId,
            SelectedPupilId = existingId,
            SelectedPupilLabel = existingLabel,
            Hint = page.Subheading,
            BackPageId = backPageId,
            BackPageIsPupilSearch = backPageIsPupilSearch
        };
    }

    private JourneyConditionContext BuildConditionContext(RequestState journey) => new()
    {
        Journey = journey,
        User = new JourneyUserContext
        {
            OrganisationUrn = currentUserService.OrganisationUrn,
            OrganisationId = currentUserService.OrganisationId,
            OrganisationName = currentUserService.OrganisationName,
            OrganisationTypeId = currentUserService.OrganisationTypeId
        }
    };

    internal static string GetPupilName(RequestState journey) =>
        journey.SelectedPupil is { } p ? $"{p.Firstname} {p.Surname}".Trim() : string.Empty;
}
