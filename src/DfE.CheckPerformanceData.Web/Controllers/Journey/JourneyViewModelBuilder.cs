using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyViewModelBuilder(
    IQuestionFlowService flowService,
    IJourneyValidationService journeyService,
    IOptionVisibilityService optionVisibilityService,
    ICurrentUserService currentUserService) : IJourneyViewModelBuilder
{
    public SummaryViewModel BuildSummaryVm(
        Guid windowId, RequestState journey, QuestionFlowConfig config, string? conflictError = null, string? conflictErrorLink = null, bool fromBulk = false, bool fromEdit = false)
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
        // Keyed on the flow declaring a match pupil search, not on a matched pupil merely being in
        // session: only a merge journey has a second record, and a matched pupil left behind by an
        // abandoned one would otherwise surface here as "Second record to merge". Covers the
        // read-only submitted view too, which is built from this same method.
        if (matchPupilPage is not null && journey.MatchedPupil is { } mp && journey.SelectedPupil is { } sp)
        {
            firstRecordDisplay = MergeRecordDisplays.First(sp);
            secondRecordDisplay = MergeRecordDisplays.Second(mp);
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
            ConflictErrorLink = conflictErrorLink,
            FromBulk = fromBulk,
            FromEdit = fromEdit,
            PrimaryPupilPageId = primaryPupilPage?.Id,
            FirstRecordDisplay = firstRecordDisplay,
            SecondRecordDisplay = secondRecordDisplay,
            MatchedPupilPageId = matchPupilPage?.Id,
            BackPageIsPupilSearch = backPage?.Type == PageType.PupilSearch,
            BackPageAction = JourneyRouting.ActionFor(backPage?.Type),
            Enquiry = BuildEnquirySummary(journey, config, pupilName)
        };
    }

    /// <summary>
    /// The enquiry-shaped summary, or null for an amendment journey. AB#296648.
    /// </summary>
    private ResultsEnquirySummary? BuildEnquirySummary(
        RequestState journey, QuestionFlowConfig config, string pupilName)
    {
        if (journey.SelectedWhatToChange != Application.CheckYourPupilData.WhatToChange.IncorrectGrade)
            return null;

        string? Answer(string questionId) =>
            journey.QuestionAnswers.TryGetValue(questionId, out var a) ? a.TextValue : null;

        // The page each answer lives on, so a Change link follows a flow-config page rename rather
        // than pointing at a hardcoded id that no longer exists.
        string? PageAsking(string questionId) => config.Pages
            .FirstOrDefault(p => p.Questions.Any(q => q.Id == questionId))?.Id;

        return new ResultsEnquirySummary
        {
            DfeNumber = currentUserService.OrganisationLaestab,
            KeyStageLabel = KeyStageLabelFor(journey.CheckingWindow?.CheckingWindowType),
            EnquiryTypeLabel = "Incorrect grade",
            StudentName = pupilName,
            IsCohortWide = string.Equals(Answer(CohortScopeQuestionId), "yes", StringComparison.OrdinalIgnoreCase),
            CohortCount = Answer(CohortCountQuestionId),
            Result = journey.SelectedResult,
            RevisedGrade = Answer(JourneyController.RevisedGradeQuestionId),
            RevisedGradePageId = PageAsking(JourneyController.RevisedGradeQuestionId),
            AdditionalInformation = Answer(AdditionalInfoQuestionId),
            AdditionalInformationPageId = PageAsking(AdditionalInfoQuestionId)
        };
    }

    // Question ids from IncorrectGrade_Post16.json. A serialization contract — see the flow tests.
    private const string CohortScopeQuestionId = "q-cohort-scope";
    private const string CohortCountQuestionId = "q-cohort-count";
    private const string AdditionalInfoQuestionId = "q-additional-info";

    /// <summary>How a window type is named to a school on the summary.</summary>
    private static string KeyStageLabelFor(Domain.Enums.CheckingWindowType? windowType) => windowType switch
    {
        Domain.Enums.CheckingWindowType.Post16 => "16 to 19",
        Domain.Enums.CheckingWindowType.KS4June or Domain.Enums.CheckingWindowType.KS4Autumn => "Key stage 4",
        Domain.Enums.CheckingWindowType.KS2 => "Key stage 2",
        _ => string.Empty
    };

    public PageViewModel BuildPageVm(
        Guid windowId,
        JourneyPage page,
        Dictionary<string, QuestionAnswer> answers,
        RequestState journey,
        bool fromSummary,
        ModelStateDictionary modelState,
        QuestionFlowConfig? config = null,
        string? uploadError = null,
        string? atLeastOneError = null,
        GradeReference? gradeReference = null)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(page.Id);
        var backPageId = historyIndex switch
        {
            -1 => journey.QuestionHistory.LastOrDefault(),
            0  => null,
            _  => journey.QuestionHistory[historyIndex - 1]
        };
        var backPage = backPageId is not null && config is not null
            ? flowService.GetPage(config, backPageId)
            : null;
        var backPageIsPupilSearch = backPage?.Type == PageType.PupilSearch;

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
                VisibleOptions = q.Type switch
                {
                    QuestionType.Radio => optionVisibilityService.GetVisibleOptions(q, conditionContext),
                    // AB#297130: grades come from the AODC reference data for the selected result's
                    // QAN, not from the flow config — the config cannot know which qualification the
                    // user picked. Pass grades before fail grades, source order preserved within each.
                    QuestionType.GradeSelect => GradeOptions(gradeReference),
                    _ => q.Options ?? []
                },
                // AB#296081: request-wide file names for the selection-time duplicate warning.
                ExistingFileNames = q.Type == QuestionType.FileUpload
                    ? answers.Values.SelectMany(qa => qa.FileValues ?? []).Select(f => f.OriginalFileName).ToList()
                    : []
            };
        }).ToList();

        return new PageViewModel
        {
            WindowId = windowId,
            Page = page,
            Answers = answers,
            BackPageId = backPageId,
            BackPageIsPupilSearch = backPageIsPupilSearch,
            BackPageAction = JourneyRouting.ActionFor(backPage?.Type),
            WhatToChange = journey.SelectedWhatToChange,
            SelectedResult = journey.SelectedResult,
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

        var (backPageId, backPage) = ResolveBackPage(pageId, journey, config);

        return new PupilSearchViewModel
        {
            WindowId = windowId,
            PageId = pageId,
            Title = title,
            Filter = page.PupilFilter ?? PupilFilter.Included,
            RequireResults = page.RequireResults,
            ExcludePupilId = excludeId,
            SelectedPupilId = existingId,
            SelectedPupilLabel = existingLabel,
            Hint = page.Subheading,
            BackPageId = backPageId,
            BackPageIsPupilSearch = backPage?.Type == PageType.PupilSearch,
            BackPageAction = JourneyRouting.ActionFor(backPage?.Type)
        };
    }

    public ResultSearchViewModel BuildResultSearchVm(
        Guid windowId, string pageId, JourneyPage page, RequestState journey, QuestionFlowConfig config,
        IReadOnlyList<StudentResultRecord> availableResults)
    {
        var pupilName = GetPupilName(journey);
        var (backPageId, backPage) = ResolveBackPage(pageId, journey, config);

        return new ResultSearchViewModel
        {
            WindowId = windowId,
            PageId = pageId,
            Title = page.Title is not null ? JourneyTemplate.Resolve(page.Title, pupilName) : string.Empty,
            Hint = page.Subheading,
            SelectedResultKey = journey.SelectedResult?.CompositeKey,
            SelectedResult = journey.SelectedResult,
            AvailableResults = availableResults,
            BackPageId = backPageId,
            BackPageAction = JourneyRouting.ActionFor(backPage?.Type)
        };
    }

    public QualificationSearchViewModel BuildQualificationSearchVm(
        Guid windowId, string pageId, JourneyPage page, RequestState journey, QuestionFlowConfig config,
        QualificationReferenceLookup lookup, string? selectedAo = null, string? selectedQan = null)
    {
        var pupilName = GetPupilName(journey);
        var (backPageId, backPage) = ResolveBackPage(pageId, journey, config);

        return new QualificationSearchViewModel
        {
            WindowId = windowId,
            PageId = pageId,
            Page = page,
            PupilName = pupilName,
            CypmdId = journey.SelectedPupil?.Cypmd_Id,
            AwardingOrganisations = lookup.AwardingOrganisations,
            Qualifications = [.. lookup.AwardingOrganisations.SelectMany(lookup.ForAwardingOrganisation)],
            SelectedAo = selectedAo ?? journey.SelectedQualification?.AwardingOrganisation,
            SelectedQan = selectedQan ?? journey.SelectedQualification?.Qan,
            BackPageId = backPageId,
            BackPageAction = JourneyRouting.ActionFor(backPage?.Type)
        };
    }

    /// <summary>
    /// The page the Back link returns to: the entry before this one in the answered history, or the
    /// last entry when this page is not in the history yet (a forward navigation).
    /// </summary>
    private (string? PageId, JourneyPage? Page) ResolveBackPage(
        string pageId, RequestState journey, QuestionFlowConfig config)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(pageId);
        var backEntry = historyIndex > 0
            ? journey.QuestionHistory[historyIndex - 1]
            : historyIndex < 0 && journey.QuestionHistory.Count > 0
                ? journey.QuestionHistory[^1]
                : null;

        return backEntry is null ? (null, null) : (backEntry, flowService.GetPage(config, backEntry));
    }

    /// <summary>
    /// The qualification's grades as picker options, pass grades before fail grades. Empty when the
    /// QAN is missing from the reference data, which the view reports rather than rendering an empty
    /// control with no explanation.
    /// </summary>
    private static IReadOnlyList<QuestionOption> GradeOptions(GradeReference? reference) =>
        reference is null
            ? []
            : [.. reference.AllGrades.Select(g => new QuestionOption { Value = g, Label = g })];

    private JourneyConditionContext BuildConditionContext(RequestState journey) =>
        JourneyConditionContextFactory.Create(journey, currentUserService);

    internal static string GetPupilName(RequestState journey) =>
        journey.SelectedPupil is { } p ? $"{p.Firstname} {p.Surname}".Trim() : string.Empty;
}
