using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.FileStorage;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyController(
    IQuestionFlowService flowService,
    IJourneyValidationService journeyService,
    IFileStorageService fileStorageService,
    IRequestService requestService,
    ICheckYourPupilDataService pupilDataService,
    IJourneyViewModelBuilder viewModelBuilder,
    IAnalyticsService analytics,
    ICurrentUserService currentUserService,
    IOptionVisibilityService optionVisibilityService,
    IQuestionOptionalityService optionalityService,
    IOriginCountryLanguageCapture originCountryLanguageCapture,
    IStudentResultsClient studentResultsClient,
    IGradeReferenceClient gradeReferenceClient,
    IQualificationReferenceClient qualificationReferenceClient,
    IRequestNotificationService requestNotificationService,
    ICheckingExerciseService checkingExerciseService,
    ILogger<JourneyController> logger) : Controller
{
    internal static string FieldName(string questionId) => $"q_{questionId.Replace("-", "_")}";

    /// <summary>
    /// AB#296648: the revised-grade question id from <c>IncorrectGrade_Post16.json</c>. Named here
    /// because changing the selected result has to clear this one answer specifically.
    /// </summary>
    internal const string RevisedGradeQuestionId = "q-revised-grade";

    /// <summary>AB#297848: the missing-qualification flow's syllabus and grade question ids. Named
    /// here because choosing a different qualification has to clear these two answers specifically.</summary>
    internal const string SyllabusQuestionId = "q-syllabus-code";
    internal const string MissingGradeQuestionId = "q-missing-grade";

    private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB

    // ── Page (GET) ──────────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/page/{pageId}")]
    public async Task<IActionResult> Page(Guid windowId, string pageId, bool fromSummary = false)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null) return NotFound();

        if (page.Type == PageType.PupilSearch)
            return RedirectToAction(nameof(PupilSearchPage), new { windowId, pageId });

        // AB#296648: the result search posts a composite result key rather than answers, so it has
        // its own action — the same reason PupilSearch does.
        if (page.Type == PageType.ResultSearch)
            return RedirectToAction(nameof(ResultSearchPage), new { windowId, pageId });

        // AB#297848: the qualification search resolves an AO+QAN pair server-side, the same reason
        // ResultSearch has its own action.
        if (page.Type == PageType.QualificationSearch)
            return RedirectToAction(nameof(QualificationSearchPage), new { windowId, pageId });

        var nav = flowService.GetNavigationGuard(config, journey, pageId);
        if (nav is RedirectToJourneySummary) return RedirectToAction(nameof(Summary), new { windowId });
        if (nav is RedirectToJourneyPage { PageId: var navPageId })
            return RedirectToAction(nameof(Page), new { windowId, pageId = navPageId });

        var viewName = page.Type switch
        {
            PageType.EvidenceUpload => "EvidenceUpload",
            // A question page that also displays the selected result — served by this action so it
            // inherits PagePost's answer handling, but rendered by its own view.
            PageType.ResultDetails => "ResultDetails",
            // AB#297848: same arrangement as ResultDetails — a question page with its own
            // summary card, served here so it inherits PagePost's answer handling.
            PageType.QualificationDetails => "QualificationDetails",
            _ => "Page"
        };
        // Surface an upload error stashed by UploadFile before its PRG redirect here — otherwise
        // a rejected upload (e.g. a non-PDF) would silently show no validation message.
        return View(viewName, viewModelBuilder.BuildPageVm(windowId, page, journey.QuestionAnswers,
            journey, fromSummary, ModelState, config,
            uploadError: TempData["UploadError"] as string,
            gradeReference: await GetGradeReferenceAsync(page, journey)));
    }

    // ── PupilSearchPage (GET) ───────────────────────────────────────────────

    [Route("/Journey/{windowId}/pupil-search/{pageId}")]
    public async Task<IActionResult> PupilSearchPage(Guid windowId, string pageId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null || page.Type != PageType.PupilSearch) return NotFound();

        var nav = flowService.GetNavigationGuard(config, journey, pageId);
        if (nav is RedirectToJourneySummary) return RedirectToAction(nameof(Summary), new { windowId });
        if (nav is RedirectToJourneyPage { PageId: var navPageId })
            return RedirectToJourneyAction(config, windowId, navPageId);

        return View("PupilSearch", viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config));
    }

    // ── PupilSearchPage (POST) ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/pupil-search/{pageId}")]
    public async Task<IActionResult> PupilSearchPost(Guid windowId, string pageId, string? selectedPupilId, string? selectedPupilLabel)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null || page.Type != PageType.PupilSearch) return NotFound();

        if (string.IsNullOrEmpty(selectedPupilId) || !Guid.TryParse(selectedPupilId, out var pupilId))
        {
            var validationMessage = page.ValidationFailure is not null
                ? JourneyTemplate.Resolve(page.ValidationFailure, JourneyViewModelBuilder.GetPupilName(journey))
                : "Enter the name of the pupil";
            ModelState.AddModelError("selectedPupilId", validationMessage);
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = 1,
                ErrorCodes = [ValidationErrorCoding.NoSelection],
                ErrorFields = ["selectedPupilId"],
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
            });
            return View("PupilSearch", viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config));
        }

        if (page.PupilKey == JourneyPage.MatchKey && selectedPupilId == journey.SelectedPupilId)
        {
            var validationMessage = page.ValidationFailure is not null
                ? JourneyTemplate.Resolve(page.ValidationFailure, JourneyViewModelBuilder.GetPupilName(journey))
                : "Select a different pupil to the first record";
            ModelState.AddModelError("selectedPupilId", validationMessage);
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = 1,
                ErrorCodes = [ValidationErrorCoding.SamePupil],
                ErrorFields = ["selectedPupilId"],
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
            });
            return View("PupilSearch", viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config));
        }

        var pupil = await pupilDataService.GetPupilAsync(windowId, pupilId);

        // AB#296648: the one-request-per-pupil rule belongs to the pupil-data checking exercise —
        // a results enquiry and a pupil-data amendment may legitimately coexist for the same pupil.
        var isResultsEnquiry = journey.SelectedWhatToChange is { } whatToChange
            && WhatToChangeCheckingExerciseMap.CheckingExerciseFor(whatToChange)
                == CheckingExerciseType.ResultsEnquiry;

        if (page.PupilKey != JourneyPage.MatchKey && !isResultsEnquiry)
        {
            var result = await requestService.HasSubmittedRequestAsync(windowId, pupil.Id, long.Parse(currentUserService.OrganisationUrn));
            if (result is not DuplicateCheckResult.NoConflict)
            {
                var isSelf = result is DuplicateCheckResult.SelfSubmitted;
                var conflictingReasonType = result switch
                {
                    DuplicateCheckResult.SelfSubmitted { ConflictingReasonType: var rt } => rt,
                    DuplicateCheckResult.OtherSubmitted { ConflictingReasonType: var rt } => rt,
                    _ => string.Empty
                };
                var conflictingCategory = result switch
                {
                    DuplicateCheckResult.SelfSubmitted { ConflictingRequestCategory: var rc } => rc,
                    DuplicateCheckResult.OtherSubmitted { ConflictingRequestCategory: var rc } => rc,
                    _ => string.Empty
                };
                var conflictingUserName = result switch
                {
                    DuplicateCheckResult.SelfSubmitted { ConflictingUserName: var un } => un,
                    DuplicateCheckResult.OtherSubmitted { ConflictingUserName: var un } => un,
                    _ => string.Empty
                };
                var currentReasonType = flowService.ResolveRequestType(config, journey);
                var reasonsMatch = !string.IsNullOrEmpty(currentReasonType)
                    && string.Equals(currentReasonType, conflictingReasonType, StringComparison.OrdinalIgnoreCase);

                ModelState.AddModelError("selectedPupilId", DuplicateRequestMessages.FieldErrorMessage);
                ModelState.AddModelError(string.Empty, DuplicateRequestMessages.ErrorSummaryMessage);
                await analytics.TrackSafeAsync(new ValidationErrorEvent
                {
                    ErrorCount = 1,
                    ErrorCodes = [ValidationErrorCoding.Conflict],
                    ErrorFields = ["selectedPupilId"],
                    WhatToChange = journey.SelectedWhatToChange?.ToString(),
                });
                var pupilName = $"{pupil.Firstname} {pupil.Surname}".Trim();
                var vm = viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config);

                var refNum = result switch
                {
                    DuplicateCheckResult.SelfSubmitted { ReferenceNumber: var r } => r,
                    DuplicateCheckResult.OtherSubmitted { ReferenceNumber: var r } => r,
                    _ => string.Empty
                };
                var linkUrl = $"/{windowId}/AmendmentRequests/{refNum}/view";
                vm.ConflictErrorReference = refNum;
                vm.ConflictErrorLink = linkUrl;
                vm.ConflictPupilName = pupilName;
                vm.ConflictReasonType = conflictingReasonType;
                vm.ConflictUserName = conflictingUserName;
                vm.ConflictAttentionHtml = DuplicateRequestMessages.AttentionBannerHtml(
                    isSelf, reasonsMatch, conflictingCategory, pupilName, refNum, linkUrl, conflictingUserName);

                return View("PupilSearch", vm);
            }
        }

        if (page.PupilKey == JourneyPage.MatchKey)
        {
            HttpContext.Session.SaveRequestState(windowId, s =>
            {
                s.MatchedPupilId = selectedPupilId;
                s.MatchedPupilLabel = selectedPupilLabel;
                s.MatchedPupil = pupil;
                if (!s.QuestionHistory.Contains(pageId))
                    s.QuestionHistory.Add(pageId);
            });
        }
        else
        {
            // AB#296648: an enquiry's reference carries an RE segment so support staff can tell it
            // from an amendment when a school reads it out.
            var reference = journey.SelectedWhatToChange == Application.CheckYourPupilData.WhatToChange.IncorrectGrade
                ? journeyService.GenerateEnquiryReference()
                : journeyService.GenerateReference(journey.CheckingWindow?.CheckingWindowType);

            // Choosing the primary pupil discards everything that was answered ABOUT a pupil — but
            // only what came after this page. On the amendment journeys the pupil page is first, so
            // that is every answer, which is what this used to do unconditionally. The enquiry journey
            // asks about the cohort BEFORE the pupil, and those answers are not about the pupil at
            // all: wiping them lost the cohort scope and count, and the summary then silently showed
            // a single-pupil enquiry (AB#296648).
            var answersToKeep = AnswersAnsweredBefore(journey, config, pageId);
            var historyBefore = journey.QuestionHistory.TakeWhile(id => id != pageId).ToList();

            HttpContext.Session.SaveRequestState(windowId, s =>
            {
                s.SelectedPupilId = selectedPupilId;
                s.SelectedPupilLabel = selectedPupilLabel;
                s.SelectedPupil = pupil;
                s.ReferenceNumber = reference;
                s.QuestionAnswers = answersToKeep;
                // A result belongs to one pupil, so a pupil change invalidates it. Fail-closed
                // re-resolution on the result page would catch a stale key, but the summary and the
                // grade page read SelectedResult directly.
                s.SelectedResult = null;
                s.QuestionHistory = [.. historyBefore, pageId];
                s.MatchedPupil = null;
                s.MatchedPupilId = null;
                s.MatchedPupilLabel = null;
            });
        }

        if (page.NextPageId is null)
            return RedirectToAction(nameof(Summary), new { windowId });

        // Routed through the shared helper: on the incorrect-grade journey the page after a pupil
        // search is a ResultSearch, which has its own action too.
        return RedirectToJourneyAction(config, windowId, page.NextPageId);
    }

    // ── ResultSearchPage (GET) ──────────────────────────────────────────────

    [Route("/Journey/{windowId}/result-search/{pageId}")]
    public async Task<IActionResult> ResultSearchPage(Guid windowId, string pageId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null || page.Type != PageType.ResultSearch) return NotFound();

        var nav = flowService.GetNavigationGuard(config, journey, pageId);
        if (nav is RedirectToJourneySummary) return RedirectToAction(nameof(Summary), new { windowId });
        if (nav is RedirectToJourneyPage { PageId: var navPageId })
            return RedirectToJourneyAction(config, windowId, navPageId);

        var available = await GetPupilResultsAsync(windowId, journey);
        return View("ResultSearch",
            viewModelBuilder.BuildResultSearchVm(windowId, pageId, page, journey, config, available));
    }

    // ── ResultSearchPage (POST) ─────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/result-search/{pageId}")]
    public async Task<IActionResult> ResultSearchPost(
        Guid windowId, string pageId, string? selectedResultKey, CancellationToken ct = default)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null || page.Type != PageType.ResultSearch) return NotFound();

        // The posted key is a claim, not a fact: re-resolve it against the results this pupil
        // actually holds. Anything that does not resolve — forged, stale, or belonging to another
        // pupil — is treated exactly as if nothing was selected (fail closed, per PBI 292525).
        var available = await GetPupilResultsAsync(windowId, journey, ct);
        var resolved = string.IsNullOrWhiteSpace(selectedResultKey)
            ? null
            : available.FirstOrDefault(r =>
                string.Equals(r.CompositeKey, selectedResultKey.Trim(), StringComparison.Ordinal));

        if (resolved is null)
        {
            var validationMessage = page.ValidationFailure is not null
                ? JourneyTemplate.Resolve(page.ValidationFailure, JourneyViewModelBuilder.GetPupilName(journey))
                : "Enter which result is incorrect";
            ModelState.AddModelError("selectedResultKey", validationMessage);
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = 1,
                ErrorCodes = [ValidationErrorCoding.NoSelection],
                ErrorFields = ["selectedResultKey"],
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
            });
            return View("ResultSearch",
                viewModelBuilder.BuildResultSearchVm(windowId, pageId, page, journey, config, available));
        }

        // A revised grade belongs to one result. Changing the result must not carry a grade over to a
        // qualification the user never chose it for — but re-confirming the same result is not a
        // change, so the grade survives back-navigation.
        var resultChanged = journey.SelectedResult?.CompositeKey != resolved.CompositeKey;

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedResult = resolved;
            if (resultChanged)
                s.QuestionAnswers.Remove(RevisedGradeQuestionId);
        });

        journey = HttpContext.Session.GetRequestState(windowId);
        TrimHistoryTo(journey, windowId, pageId);

        if (page.NextPageId is null)
            return RedirectToAction(nameof(Summary), new { windowId });

        return RedirectToJourneyAction(config, windowId, page.NextPageId);
    }

    // ── QualificationSearchPage (GET) — AB#297848 ───────────────────────────

    [Route("/Journey/{windowId}/qualification-search/{pageId}")]
    public async Task<IActionResult> QualificationSearchPage(Guid windowId, string pageId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null || page.Type != PageType.QualificationSearch) return NotFound();

        var nav = flowService.GetNavigationGuard(config, journey, pageId);
        if (nav is RedirectToJourneySummary) return RedirectToAction(nameof(Summary), new { windowId });
        if (nav is RedirectToJourneyPage { PageId: var navPageId })
            return RedirectToJourneyAction(config, windowId, navPageId);

        var lookup = await qualificationReferenceClient.GetLookupAsync(HttpContext.RequestAborted);
        return View("QualificationSearch",
            viewModelBuilder.BuildQualificationSearchVm(windowId, pageId, page, journey, config, lookup));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/qualification-search/{pageId}")]
    public async Task<IActionResult> QualificationSearchPost(
        Guid windowId, string pageId, string? selectedAo, string? selectedQan)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null || page.Type != PageType.QualificationSearch) return NotFound();

        var lookup = await qualificationReferenceClient.GetLookupAsync(HttpContext.RequestAborted);

        // Resolve server-side and fail closed: the QAN must exist AND belong to the posted AO —
        // the client-side cascade is presentation only, and a tampered pair would otherwise record
        // an AO the qualification does not belong to.
        var resolved = string.IsNullOrWhiteSpace(selectedQan) ? null : lookup.Find(selectedQan);
        if (resolved is not null && !string.Equals(resolved.AwardingOrganisation, selectedAo, StringComparison.Ordinal))
            resolved = null;

        if (string.IsNullOrWhiteSpace(selectedAo))
            ModelState.AddModelError("selectedAo", "Select the Awarding Organisation (AO) name");
        if (resolved is null)
            ModelState.AddModelError("selectedQan", "Select the Qualification Number (QAN)");

        if (!ModelState.IsValid)
        {
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = ModelState.ErrorCount,
                ErrorCodes = [.. Enumerable.Repeat(ValidationErrorCoding.NoSelection, ModelState.ErrorCount)],
                ErrorFields = [.. ModelState.Keys],
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
            });
            return View("QualificationSearch", viewModelBuilder.BuildQualificationSearchVm(
                windowId, pageId, page, journey, config, lookup, selectedAo, selectedQan));
        }

        // Syllabus code and grade belong to one qualification: changing it must not carry them to
        // a qualification that never offered them. Re-confirming the same QAN is not a change.
        var qualificationChanged = journey.SelectedQualification?.Qan != resolved!.Qan;
        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedQualification = resolved;
            if (qualificationChanged)
            {
                s.QuestionAnswers.Remove(SyllabusQuestionId);
                s.QuestionAnswers.Remove(MissingGradeQuestionId);
            }
        });

        journey = HttpContext.Session.GetRequestState(windowId);
        TrimHistoryTo(journey, windowId, pageId);

        if (page.NextPageId is null) return RedirectToAction(nameof(Summary), new { windowId });
        return RedirectToJourneyAction(config, windowId, page.NextPageId);
    }

    /// <summary>
    /// The answers belonging to pages the user visited BEFORE <paramref name="pageId"/>. Used when a
    /// pupil selection invalidates everything asked about that pupil, without discarding answers that
    /// were never about them.
    /// </summary>
    private Dictionary<string, QuestionAnswer> AnswersAnsweredBefore(
        RequestState journey, QuestionFlowConfig config, string pageId)
    {
        var keep = journey.QuestionHistory
            .TakeWhile(id => id != pageId)
            .Select(id => flowService.GetPage(config, id))
            .Where(p => p is not null)
            .SelectMany(p => p!.Questions.Select(q => q.Id))
            .ToHashSet(StringComparer.Ordinal);

        return journey.QuestionAnswers
            .Where(kv => keep.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// The page a results enquiry must go back to before it can be reviewed, or null when it is
    /// complete. Returns the earliest gap so the user is sent to the first thing they still owe.
    /// Always null for an amendment journey.
    /// </summary>
    private static string? FirstIncompleteEnquiryPage(RequestState journey, QuestionFlowConfig config)
    {
        if (journey.SelectedWhatToChange != Application.CheckYourPupilData.WhatToChange.IncorrectGrade)
            return null;

        var resultPage = config.Pages.FirstOrDefault(p => p.Type == PageType.ResultSearch);
        if (journey.SelectedResult is null)
            return resultPage?.Id;

        var gradePage = config.Pages.FirstOrDefault(
            p => p.Questions.Any(q => q.Id == RevisedGradeQuestionId));
        var hasGrade = journey.QuestionAnswers.TryGetValue(RevisedGradeQuestionId, out var grade)
                       && !string.IsNullOrWhiteSpace(grade.TextValue);

        return hasGrade ? null : gradePage?.Id;
    }

    /// <summary>
    /// The grade scale for the selected result's qualification, or null when the page has no grade
    /// picker or the QAN is absent from the AODC reference data. A gap is logged rather than thrown:
    /// the page tells the user grades cannot be listed yet, and validation holds the enquiry back.
    /// </summary>
    private async Task<GradeReference?> GetGradeReferenceAsync(
        JourneyPage page, RequestState journey, CancellationToken ct = default)
    {
        if (page.Questions.All(q => q.Type != QuestionType.GradeSelect)) return null;

        // AB#297848: on a missing-qualification enquiry the scale comes from the QualList entry
        // resolved at qualification selection — there is no exam result and no AODC lookup.
        if (journey.SelectedResult is null && journey.SelectedQualification is { } qualification)
            return qualification.ToGradeReference();

        var qan = journey.SelectedResult?.Qan;
        if (string.IsNullOrWhiteSpace(qan)) return null;

        var reference = await gradeReferenceClient.GetByQanAsync(qan, ct);
        if (reference is null)
            logger.LogWarning(
                "No AODC grade reference for QAN {Qan}; the revised-grade picker on page {PageId} will " +
                "be empty and the enquiry cannot be submitted until the reference data covers it.",
                qan, page.Id);

        return reference;
    }

    /// <summary>
    /// Every result the journey's selected pupil holds at the signed-in school. Empty when no pupil
    /// has been chosen yet — which both renders an empty picker and makes any posted key unresolvable.
    /// </summary>
    private async Task<IReadOnlyList<StudentResultRecord>> GetPupilResultsAsync(
        Guid windowId, RequestState journey, CancellationToken ct = default)
    {
        var cypmdId = journey.SelectedPupil?.Cypmd_Id;
        if (string.IsNullOrWhiteSpace(cypmdId)) return [];

        return await studentResultsClient.GetResultsAsync(
            windowId, currentUserService.OrganisationLaestab, cypmdId, ct);
    }

    // ── Page (POST — Continue) ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/page/{pageId}")]
    public async Task<IActionResult> PagePost(Guid windowId, string pageId, bool fromSummary,
        IFormFile? fileUpload = null)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        var page = flowService.GetPage(config, pageId);
        if (page is null) return NotFound();

        var newAnswers = new Dictionary<string, QuestionAnswer>();
        var pupilName = JourneyViewModelBuilder.GetPupilName(journey);
        // Resolved once for the page rather than per question: the lookup is async and cached, and a
        // page has at most one grade picker.
        var gradeReference = await GetGradeReferenceAsync(page, journey);
        var isValid = true;
        var conditionContext = JourneyConditionContextFactory.Create(journey, currentUserService);
        var conditionallyOptional = optionalityService.GetConditionallyOptionalQuestionIds(page, conditionContext);
        bool IsMandatory(Question q) => !q.Optional && !conditionallyOptional.Contains(q.Id);

        // Commit a file the user selected in Browse but didn't click "Upload file" for — clicking
        // Continue with a file staged should attach it rather than report "upload a file".
        string? pendingUploadError = null;
        var fileQuestion = page.Questions.FirstOrDefault(q => q.Type == QuestionType.FileUpload);
        if (fileUpload is { Length: > 0 } && fileQuestion is not null)
        {
            pendingUploadError = await CommitUploadedFileAsync(windowId, fileQuestion.Id, journey, fileUpload);
            if (pendingUploadError is not null) isValid = false;
        }

        foreach (var question in page.Questions)
        {
            if (question.Type == QuestionType.FileUpload)
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var existing);
                var files = existing?.FileValues ?? [];
                // When a staged file failed validation the upload error already explains the
                // problem — don't also tell the user to upload a file for the same field.
                var explainedByUploadError = pendingUploadError is not null && question.Id == fileQuestion?.Id;
                if (files.Count == 0 && IsMandatory(question) && !explainedByUploadError)
                {
                    ModelState.AddModelError(question.Id, "Upload at least one file before continuing");
                    isValid = false;
                }
                else
                {
                    newAnswers[question.Id] = new QuestionAnswer { FileValues = files };
                }
            }
            else
            {
                var answer = ReadFormAnswer(question);

                // visibleWhen gates selection as well as rendering: a posted radio value
                // that is not among this user's visible options (hidden by a condition,
                // or not a defined option at all) is rejected with the question's own
                // validation message, exactly as if nothing was selected. This backs the
                // add-back policy restriction (PBI 292525) server-side.
                if (question.Type == QuestionType.Radio && question.Options is { Count: > 0 }
                    && journeyService.IsAnswered(question, answer)
                    && optionVisibilityService.GetVisibleOptions(question, conditionContext)
                        .All(o => o.Value != answer.TextValue))
                {
                    var hiddenFailure = question.ValidationFailure is not null
                        ? JourneyTemplate.Resolve(question.ValidationFailure, pupilName)
                        : "Select an option";
                    ModelState.AddModelError(question.Id, hiddenFailure);
                    isValid = false;
                    newAnswers[question.Id] = answer;
                    continue;
                }

                // Required answers are validated unconditionally; optional answers are
                // still format-checked (char limit, real date) when they have been filled in.
                if (IsMandatory(question) || journeyService.IsAnswered(question, answer))
                {
                    var resolvedValidationFailure = question.ValidationFailure is not null
                        ? JourneyTemplate.Resolve(question.ValidationFailure, pupilName) : null;

                    // A grade picker has its own rules and its own inputs (the qualification's scale
                    // and the grade the result already holds), so it does not go through the generic
                    // answer validator. AB#296648.
                    var error = question.Type switch
                    {
                        QuestionType.GradeSelect => journeyService.ValidateGradeSelect(
                            question, answer, gradeReference, journey.SelectedResult?.Grade, resolvedValidationFailure),
                        // AB#297848: syllabus options live on the resolved qualification; a QAN
                        // with none (961 of 974 today) rejects everything and the page explains
                        // the gap. Membership is on the code alone — the title is display-only.
                        QuestionType.SyllabusSelect => journeyService.ValidateOptionSelect(
                            question, answer,
                            journey.SelectedQualification?.SyllabusCodes.Select(c => c.Code).ToList() ?? [],
                            resolvedValidationFailure),
                        _ => journeyService.ValidateAnswer(
                            question, answer, JourneyTemplate.Resolve(question.Title, pupilName), resolvedValidationFailure)
                    };

                    if (error is not null)
                    {
                        ModelState.AddModelError(question.Id, error);
                        isValid = false;
                    }
                }
                newAnswers[question.Id] = answer;
            }
        }

        // Cross-field date rules (AB#295246). Runs after the loop because it needs every answer
        // on the page, and skips any question that already failed its own format check — the
        // view model renders only the first error per question, so adding a second here would
        // replace "must be a real date" with a comparison against a date the user never entered.
        //
        // The Add rules (AB#297310) compare date of birth against admission date, which sit on
        // different pages, so the stored answers go in underneath the posted ones — the page's
        // own questions are all present in newAnswers, so this page always wins for its fields.
        var answersInScope = journey.QuestionAnswers
            .Concat(newAnswers)
            .GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        var dateRuleQuestionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var violation in journeyService.ValidatePageDates(page, answersInScope, pupilName))
        {
            if (ModelState.TryGetValue(violation.QuestionId, out var existing) && existing.Errors.Count > 0)
                continue;
            ModelState.AddModelError(violation.QuestionId, violation.Message);
            dateRuleQuestionIds.Add(violation.QuestionId);
            isValid = false;
        }

        // Page-level "at least one answered" rule (e.g. EvidenceUpload pages).
        var atLeastOne = journeyService.ValidateRequireAtLeastOne(page, newAnswers, pupilName);
        if (atLeastOne is not null)
        {
            foreach (var (qId, msg) in atLeastOne.FieldErrors)
                ModelState.AddModelError(qId, msg);
            isValid = false;
        }

        if (!isValid)
        {
            var codes = new List<string>();
            var fields = new List<string>();
            foreach (var q in page.Questions)
            {
                if (!ModelState.TryGetValue(q.Id, out var entry) || entry.Errors.Count == 0) continue;
                if (q.Type == QuestionType.FileUpload) { codes.Add(ValidationErrorCoding.FileRequired); fields.Add(q.Id); continue; }
                // A cross-field failure is a well-formed date in the wrong place, not a malformed
                // one — coding it as bad_date would hide the distinction the rule exists to make.
                if (dateRuleQuestionIds.Contains(q.Id)) { codes.Add(ValidationErrorCoding.DateInconsistent); fields.Add(q.Id); continue; }
                newAnswers.TryGetValue(q.Id, out var ans);
                var answered = ans is not null && journeyService.IsAnswered(q, ans);
                codes.Add(ValidationErrorCoding.ForQuestion(q, answered));
                fields.Add(q.Id);
            }
            if (atLeastOne is not null) { codes.Add(ValidationErrorCoding.AtLeastOne); fields.Add("page"); }
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = ModelState.ErrorCount,
                ErrorCodes = codes,
                ErrorFields = fields,
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
                FromSummary = fromSummary,
            });

            var displayAnswers = journey.QuestionAnswers
                .Concat(newAnswers)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);
            var invalidViewName = page.Type switch
            {
                PageType.EvidenceUpload => "EvidenceUpload",
                PageType.ResultDetails => "ResultDetails",
            // AB#297848: same arrangement as ResultDetails — a question page with its own
            // summary card, served here so it inherits PagePost's answer handling.
            PageType.QualificationDetails => "QualificationDetails",
                _ => "Page"
            };
            return View(invalidViewName, viewModelBuilder.BuildPageVm(windowId, page, displayAnswers,
                journey, fromSummary, ModelState, config,
                uploadError: pendingUploadError ?? TempData["UploadError"] as string,
                atLeastOneError: atLeastOne?.SummaryMessage,
                gradeReference: gradeReference));
        }

        if (page.Type == PageType.EvidenceUpload)
        {
            var files = page.Questions
                .Where(q => q.Type == QuestionType.FileUpload)
                .SelectMany(q => newAnswers.TryGetValue(q.Id, out var a) ? (a.FileValues ?? []) : [])
                .ToList();
            var textLength = page.Questions
                .Where(q => q.Type == QuestionType.TextArea)
                .Sum(q => newAnswers.TryGetValue(q.Id, out var a) ? (a.TextValue?.Length ?? 0) : 0);
            await analytics.TrackSafeAsync(new EvidenceContinueEvent
            {
                FileCount = files.Count,
                PageCount = files.Sum(f => f.PageCount),
                EvidenceTextLength = textLength,
            });
        }

        // Store the origin country's official languages whenever this page (re-)answers
        // the country question, so the evidence page's optionality condition and the
        // rules engine read consistent facts (PBI 292266).
        await originCountryLanguageCapture.ApplyAsync(journey, newAnswers, HttpContext.RequestAborted);

        if (fromSummary)
        {
            var oldNextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);

            foreach (var (qId, answer) in newAnswers)
                journey.QuestionAnswers[qId] = answer;

            HttpContext.Session.SaveRequestState(windowId, s =>
            {
                s.QuestionAnswers = journey.QuestionAnswers;
                s.OriginCountryCode = journey.OriginCountryCode;
                s.OriginCountryLanguages = journey.OriginCountryLanguages;
            });
            MintSyntheticPupilIfNeeded(windowId, page);

            var newNextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);

            if (newNextId == oldNextId)
                return RedirectToAction(nameof(Summary), new { windowId });

            TrimHistoryTo(journey, windowId, pageId);
            if (newNextId is null)
                return RedirectToAction(nameof(Summary), new { windowId });

            return RedirectToAction(nameof(Page), new { windowId, pageId = newNextId });
        }

        // Capture the branch target before applying the new answers so we can tell whether
        // re-answering this page changed the flow (relevant when the user navigated back).
        var priorNextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);

        foreach (var (qId, answer) in newAnswers)
            journey.QuestionAnswers[qId] = answer;

        var nextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);

        if (journey.QuestionHistory.Contains(pageId))
        {
            // The user came back to an already-visited page. If the answer changed the branch,
            // the pages recorded after this one belong to the old branch — trim them so the
            // navigation guard doesn't recompute the next page from a stale history entry and
            // bounce the user back into that branch.
            if (nextId != priorNextId)
                TrimHistoryTo(journey, windowId, pageId);
        }
        else
        {
            journey.QuestionHistory.Add(pageId);
        }

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.QuestionAnswers = journey.QuestionAnswers;
            s.QuestionHistory = journey.QuestionHistory;
            s.OriginCountryCode = journey.OriginCountryCode;
            s.OriginCountryLanguages = journey.OriginCountryLanguages;
        });
        MintSyntheticPupilIfNeeded(windowId, page);

        return nextId is null
            ? RedirectToAction(nameof(Summary), new { windowId })
            : RedirectToAction(nameof(Page), new { windowId, pageId = nextId });
    }

    // ── File upload (POST — single file) ───────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/page/{pageId}/question/{questionId}/upload")]
    public async Task<IActionResult> UploadFile(Guid windowId, string pageId, string questionId,
        bool fromSummary, IFormFile? fileUpload)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        // Preserve any text the user typed on the page before they triggered the upload —
        // the upload posts the whole page form, so the text fields ride along and must not
        // be lost on the post-redirect re-render (even when the upload itself fails).
        await PersistPageTextAnswersAsync(windowId, pageId, journey);

        if (fileUpload is null || fileUpload.Length == 0)
        {
            TempData["UploadError"] = "Select a file to upload";
            await analytics.TrackSafeAsync(
                new EvidenceUploadAttemptedEvent { Outcome = "failed", FailureReason = "no_file" });
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        var error = await CommitUploadedFileAsync(windowId, questionId, journey, fileUpload);
        if (error is not null)
            TempData["UploadError"] = error;

        return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
    }

    // Validates and stores a single uploaded file against the given question, updating both the
    // session and the in-memory `journey` so callers in the same request see the new file.
    // Returns null on success, or a user-facing error message on failure. Assumes a non-empty file.
    private async Task<string?> CommitUploadedFileAsync(Guid windowId, string questionId, RequestState journey, IFormFile file)
    {
        // AB#296081: a request must never store two evidence files with the same name.
        // Uniqueness is per amendment request, so gather every question's files, not
        // just this question's.
        var allRequestFiles = journey.QuestionAnswers.Values
            .SelectMany(a => a.FileValues ?? [])
            .ToList();
        var duplicateError = journeyService.ValidateDuplicateFileName(file.FileName, allRequestFiles);
        if (duplicateError is not null)
        {
            await analytics.TrackSafeAsync(new EvidenceUploadAttemptedEvent { Outcome = "failed", FailureReason = "duplicate_name", FileSizeBytes = file.Length });
            return duplicateError;
        }

        if (file.Length > MaxUploadBytes)
        {
            await analytics.TrackSafeAsync(new EvidenceUploadAttemptedEvent { Outcome = "failed", FailureReason = "too_large", FileSizeBytes = file.Length });
            return $"'{file.FileName}' must be 10 MB or less";
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var pageCount = PdfPageCounter.GetPageCount(bytes);
        if (pageCount is null)
        {
            await analytics.TrackSafeAsync(new EvidenceUploadAttemptedEvent { Outcome = "failed", FailureReason = "not_a_pdf", FileSizeBytes = bytes.LongLength });
            return $"Evidence must be in a PDF format.";
        }

        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];

        var uploadError = journeyService.ValidateFileUpload(file.FileName, pageCount.Value, currentFiles);
        if (uploadError is not null)
        {
            await analytics.TrackSafeAsync(new EvidenceUploadAttemptedEvent { Outcome = "failed", FailureReason = "page_limit_exceeded", PageCount = pageCount.Value, FileSizeBytes = bytes.LongLength });
            return uploadError;
        }

        var storedName = await fileStorageService.SaveAsync(windowId, bytes);
        currentFiles.Add(new FileAnswer
        {
            StoredFileName = storedName,
            OriginalFileName = file.FileName,
            PageCount = pageCount.Value,
            FileSizeBytes = bytes.LongLength
        });

        var updated = new QuestionAnswer { FileValues = currentFiles };
        journey.QuestionAnswers[questionId] = updated;
        HttpContext.Session.SaveRequestState(windowId, s => s.QuestionAnswers[questionId] = updated);

        await analytics.TrackSafeAsync(new EvidenceUploadAttemptedEvent
        {
            Outcome = "success",
            PageCount = pageCount.Value,
            FileSizeBytes = bytes.LongLength,
        });

        return null;
    }

    // ── File remove (POST) ─────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/page/{pageId}/question/{questionId}/remove")]
    public async Task<IActionResult> RemoveFile(Guid windowId, string pageId, string questionId, bool fromSummary, string storedFileName)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        // Preserve text the user typed on the page before removing a file (see UploadFile).
        await PersistPageTextAnswersAsync(windowId, pageId, journey);

        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];

        if (currentFiles.All(f => f.StoredFileName != storedFileName))
            return BadRequest();

        var filesBefore = currentFiles.Count;
        currentFiles.RemoveAll(f => f.StoredFileName == storedFileName);

        await fileStorageService.DeleteAsync(windowId, storedFileName);

        HttpContext.Session.SaveRequestState(windowId, s =>
            s.QuestionAnswers[questionId] = new QuestionAnswer { FileValues = currentFiles });

        await analytics.TrackSafeAsync(new EvidenceFileRemovedEvent
        {
            FilesBefore = filesBefore,
            FilesAfter = currentFiles.Count,
        });

        return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
    }

    // ── Summary ────────────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/summary")]
    public async Task<IActionResult> Summary(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var config = await GetConfigAsync(journey);
        if (config is null) return RedirectToCheckYourData(windowId);

        // Redirect to start if journey hasn't been begun
        if (journey.QuestionHistory.Count == 0)
            return RedirectToAction(nameof(Page), new { windowId, pageId = config.FirstPageId });

        // Redirect to next unanswered page if journey is incomplete
        var nextExpected = flowService.GetNextPageId(config, journey.QuestionHistory.Last(), journey.QuestionAnswers);
        if (nextExpected is not null)
            return RedirectToAction(nameof(Page), new { windowId, pageId = nextExpected });

        // Evidence optionality is conditional (PBI 292266), so an answer changed from this page
        // — First language, say — can turn a waived evidence page back into a mandatory one
        // without the user passing through it again. Nothing between here and submission
        // re-validates, so re-check and send them back. Only for a page already visited:
        // GetNavigationGuard bounces an unvisited page back here once the journey is complete,
        // which would loop.
        var evidencePage = flowService.GetReachableEvidencePage(config, journey.QuestionAnswers);
        if (evidencePage is not null
            && journey.QuestionHistory.Contains(evidencePage.Id)
            && !IsEvidencePageValid(evidencePage, journey, JourneyViewModelBuilder.GetPupilName(journey)))
            return RedirectToAction(nameof(Page), new { windowId, pageId = evidencePage.Id });

        // AB#296648: a results enquiry needs a chosen result and a revised grade, neither of which
        // the flow engine's question-answer walk can see — the result lives outside QuestionAnswers,
        // and its grade is cleared when the result changes. Without this, an enquiry with no result
        // would reach the summary showing blank rows and could be submitted.
        var incompleteEnquiryPage = FirstIncompleteEnquiryPage(journey, config);
        if (incompleteEnquiryPage is not null)
            return RedirectToJourneyAction(config, windowId, incompleteEnquiryPage);

        var fromBulk = HttpContext.Session.IsBulkEditMode(windowId);
        var fromEdit = HttpContext.Session.IsSingleEditMode(windowId);
        return View(viewModelBuilder.BuildSummaryVm(windowId, journey, config, fromBulk: fromBulk, fromEdit: fromEdit));
    }

    [Route("/Journey/{windowId}/evidence/{storedFileName}")]
    public async Task<IActionResult> DownloadEvidence(Guid windowId, string storedFileName)
    {
        if (!Guid.TryParse(storedFileName, out _)) return NotFound();

        var journey = HttpContext.Session.GetRequestState(windowId);
        // #318 AC: no gated path returns 404. The link is an ordinary browser navigation, so the
        // redirect renders the explanation like every other rejected entry point.
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        var fileAnswer = journey.QuestionAnswers.Values
            .SelectMany(a => a.FileValues ?? [])
            .FirstOrDefault(f => f.StoredFileName == storedFileName);

        if (fileAnswer is null) return NotFound();

        var bytes = await fileStorageService.GetAsync(windowId, storedFileName);
        if (bytes is null) return NotFound();

        return File(bytes, "application/pdf", fileAnswer.OriginalFileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/summary")]
    public async Task<IActionResult> SummaryConfirm(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        // AB#296648: a results enquiry submits by a different route — no duplicate check (several
        // enquiries about the same result are allowed) and no rules-engine enqueue.
        if (journey.SelectedWhatToChange == Application.CheckYourPupilData.WhatToChange.IncorrectGrade)
            return await ConfirmResultsEnquiryAsync(windowId, journey);

        try
        {
            await requestService.ConfirmRequestAsync(windowId, journey);
        }
        catch (DuplicateRequestException ex)
        {
            await analytics.TrackSafeAsync(new RequestSubmissionFailedEvent
            {
                FailureReason = "duplicate_request",
                WhatToChange = journey.SelectedWhatToChange?.ToString() ?? "",
                CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
            });

            var config = await GetConfigAsync(journey);
            if (config is null) return RedirectToCheckYourData(windowId);

            var message = DuplicateRequestMessages.SummaryMessage(
                ex.ConflictType == ConflictType.SelfSubmitted, ex.ReasonsMatch,
                ex.ConflictingRequestCategory);

            string? conflictErrorLink = $"/{windowId}/AmendmentRequests/{journey.ReferenceNumber}/view";

            return View("Summary", viewModelBuilder.BuildSummaryVm(windowId, journey, config,
                conflictError: message, conflictErrorLink: conflictErrorLink,
                fromBulk: HttpContext.Session.IsBulkEditMode(windowId),
                fromEdit: HttpContext.Session.IsSingleEditMode(windowId)));
        }

        await analytics.TrackSafeAsync(new RequestSubmittedEvent
        {
            WhatToChange = journey.SelectedWhatToChange?.ToString() ?? "",
            CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
            ReferenceNumber = journey.ReferenceNumber ?? "",
        });

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedNextStep = null;
            s.SelectedWhatToChange = null;
            s.SelectedPupilId = null;
            s.SelectedPupilLabel = null;
            s.SelectedPupil = null;
            s.MatchedPupilId = null;
            s.MatchedPupilLabel = null;
            s.MatchedPupil = null;
            s.QuestionAnswers.Clear();
            s.QuestionHistory.Clear();
        });
        HttpContext.Session.ClearSingleEditMode(windowId);

        return RedirectToAction(nameof(Confirmation), new { windowId });
    }

    // ── Results enquiry submission ─────────────────────────────────────────

    /// <summary>
    /// Submits a results enquiry, then clears the journey while keeping what the confirmation page
    /// needs. AB#296648.
    /// </summary>
    private async Task<IActionResult> ConfirmResultsEnquiryAsync(Guid windowId, RequestState journey)
    {
        var reference = await requestService.SubmitResultsEnquiryAsync(windowId, journey);

        // Everything after this point is best-effort: the enquiry is already persisted, so a failure
        // to email or to record analytics must not tell the school their submission failed.
        try
        {
            await requestNotificationService.NotifyResultsEnquirySubmittedAsync(reference);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Results enquiry {ReferenceNumber} submitted but the confirmation notification failed",
                reference);
        }

        await analytics.TrackSafeAsync(new ResultsEnquirySubmittedEvent
        {
            EnquiryType = ResultIssueViewModel.IncorrectGrade,
            CohortWide = IsCohortWide(journey),
            CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
            ReferenceNumber = reference,
        });

        // The reference and the window survive so the confirmation page can render; everything the
        // user entered goes, so "Report another issue" genuinely starts clean.
        HttpContext.Session.SetRequestState(windowId, new RequestState
        {
            CheckingWindow = journey.CheckingWindow,
            ReferenceNumber = reference
        });
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.ClearSingleEditMode(windowId);

        return RedirectToAction(nameof(EnquiryConfirmation), new { windowId });
    }

    [Route("/Journey/{windowId}/enquiry-confirmation")]
    public IActionResult EnquiryConfirmation(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (string.IsNullOrWhiteSpace(journey.ReferenceNumber) || journey.CheckingWindow is null)
            return RedirectToCheckYourData(windowId);

        return View("EnquiryConfirmation", new EnquiryConfirmationViewModel
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber
        });
    }

    private static bool IsCohortWide(RequestState journey) =>
        journey.QuestionAnswers.TryGetValue("q-cohort-scope", out var scope)
        && string.Equals(scope.TextValue, "yes", StringComparison.OrdinalIgnoreCase);

    // ── Save draft ─────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/draft")]
    public async Task<IActionResult> SaveDraft(Guid windowId, string? pageId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        JourneyPage? page = null;
        if (pageId is not null)
        {
            var config = await GetConfigAsync(journey);
            page = config is not null ? flowService.GetPage(config, pageId) : null;
            if (page is not null)
            {
                // Capture any unsaved non-file answers from the form
                var newAnswers = new Dictionary<string, QuestionAnswer>();
                foreach (var question in page.Questions.Where(q => q.Type != QuestionType.FileUpload))
                {
                    var answer = ReadFormAnswer(question);
                    if (!string.IsNullOrWhiteSpace(answer.TextValue))
                        newAnswers[question.Id] = answer;
                }

                // Mirrors PagePost: recover the country code the autocomplete's hidden _code
                // field loses on a re-POST, so a "Save and exit" here doesn't overwrite the
                // good answer with CodeValue = null and leave OriginCountryLanguages stale.
                await originCountryLanguageCapture.ApplyAsync(journey, newAnswers, HttpContext.RequestAborted);

                HttpContext.Session.SaveRequestState(windowId, s =>
                {
                    foreach (var (qId, answer) in newAnswers)
                        s.QuestionAnswers[qId] = answer;
                    s.OriginCountryCode = journey.OriginCountryCode;
                    s.OriginCountryLanguages = journey.OriginCountryLanguages;
                });
                journey = HttpContext.Session.GetRequestState(windowId);
            }
        }

        var status = DetermineStatus(pageId, page, journey);

        // A ReadyToSubmit page has been completed, so record it in history exactly like
        // "Save and continue" does. Otherwise the Summary navigation guard recomputes the
        // next expected page from the last visited page and bounces the resumed request
        // back onto this page (e.g. an all-optional evidence page saved straight to draft).
        if (status == RequestStatus.ReadyToSubmit && pageId is not null && !journey.QuestionHistory.Contains(pageId))
        {
            journey.QuestionHistory.Add(pageId);
            HttpContext.Session.SaveRequestState(windowId, s => s.QuestionHistory = journey.QuestionHistory);
        }

        await requestService.SaveDraftAsync(windowId, journey, status);

        await analytics.TrackSafeAsync(new DraftSavedEvent
        {
            Status = status.ToString(),
            WhatToChange = journey.SelectedWhatToChange?.ToString() ?? "",
            CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
            ReferenceNumber = journey.ReferenceNumber ?? "",
        });

        // A draft saved mid-bulk-submit returns to the batch review rather than the list, so the
        // user can carry on submitting the batch. Capture the flag before clearing session state.
        var fromBulk = HttpContext.Session.IsBulkEditMode(windowId);

        HttpContext.Session.ClearRequestState(windowId);
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.ClearSingleEditMode(windowId);

        return fromBulk
            ? RedirectToAction("BulkReviewDetailedPage", "AmendmentRequests", new { windowId })
            : RedirectToAction("Index", "AmendmentRequests", new { windowId });
    }

    private RequestStatus DetermineStatus(string? pageId, JourneyPage? page, RequestState journey)
    {
        if (pageId is null) return RequestStatus.ReadyToSubmit;
        if (page?.Type != PageType.EvidenceUpload) return RequestStatus.InProgress;
        var pupilName = JourneyViewModelBuilder.GetPupilName(journey);
        return IsEvidencePageValid(page, journey, pupilName) ? RequestStatus.ReadyToSubmit : RequestStatus.InProgress;
    }

    private bool IsEvidencePageValid(JourneyPage page, RequestState journey, string pupilName)
    {
        var ctx = JourneyConditionContextFactory.Create(journey, currentUserService);
        var conditionallyOptional = optionalityService.GetConditionallyOptionalQuestionIds(page, ctx);
        return journeyService.ValidateEvidencePage(page, journey, pupilName, conditionallyOptional) is null;
    }

    // ── Confirmation ───────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/confirmation")]
    public IActionResult Confirmation(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);

        if (string.IsNullOrEmpty(journey?.ReferenceNumber) || journey?.CheckingWindow is null)
            return RedirectToCheckYourData(windowId);

        var model = new ConfirmationViewModel
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber,
            WindowCloseLabel = $"{journey.CheckingWindow.EndDate:htt} on {journey.CheckingWindow.EndDate:dddd d MMMM yyyy}"
        };

        return View(model);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// #318: the one gate every journey action already runs. It now also requires the journey's
    /// own checking exercise to be open, so a bookmarked URL or a tab left open across the closing
    /// date cannot post into a shut journey. The exercise is derived from
    /// <see cref="RequestState.SelectedWhatToChange"/> rather than stored: a stored copy can
    /// disagree with the journey's own change type, and adding an exercise type never has to touch
    /// this method again.
    /// </summary>
    private bool IsSessionReady(RequestState journey) =>
        journey.SelectedWhatToChange is not null &&
        journey.CheckingWindow is not null &&
        !IsExerciseClosed(journey);

    // True only when the journey is otherwise complete and its exercise has closed — the one
    // rejection reason worth explaining to the user.
    private bool IsExerciseClosed(RequestState journey) =>
        journey.SelectedWhatToChange is { } change &&
        journey.CheckingWindow is not null &&
        !checkingExerciseService.IsOpen(
            journey.CheckingWindow.Exercises,
            WhatToChangeCheckingExerciseMap.CheckingExerciseFor(change));

    /// <summary>
    /// Every bounce out of the journey. A closed exercise is explained on the page the user lands
    /// on; the other reasons (no session, no flow config) are silent, because a session that was
    /// never started has nothing to tell the user.
    /// </summary>
    private RedirectToActionResult RedirectToCheckYourData(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (IsExerciseClosed(journey))
            return this.RedirectExerciseClosed(
                windowId,
                WhatToChangeCheckingExerciseMap.CheckingExerciseFor(journey.SelectedWhatToChange!.Value));

        return RedirectToAction("Index", "CheckYourPupilData", new { windowId });
    }

    private RedirectToActionResult RedirectToJourneyAction(QuestionFlowConfig config, Guid windowId, string pageId)
    {
        var target = flowService.GetPage(config, pageId);
        return RedirectToAction(JourneyRouting.ActionFor(target?.Type), new { windowId, pageId });
    }

    private Task<QuestionFlowConfig?> GetConfigAsync(RequestState journey) =>
        flowService.GetConfigAsync(journey.SelectedWhatToChange!.Value, journey.CheckingWindow!.CheckingWindowType);

    // Persists the submitted answers for a page's non-file-upload questions to session so they
    // survive an upload/remove round-trip. No validation here (e.g. char limit) — the answers are
    // re-validated when the user submits the page via PagePost.
    private async Task PersistPageTextAnswersAsync(Guid windowId, string pageId, RequestState journey)
    {
        var config = await GetConfigAsync(journey);
        var page = config is null ? null : flowService.GetPage(config, pageId);
        if (page is null) return;

        var answers = page.Questions
            .Where(q => q.Type != QuestionType.FileUpload)
            .ToDictionary(q => q.Id, ReadFormAnswer);
        if (answers.Count == 0) return;

        foreach (var (qId, answer) in answers)
            journey.QuestionAnswers[qId] = answer;

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            foreach (var (qId, answer) in answers)
                s.QuestionAnswers[qId] = answer;
        });
    }

    // AB#297310: the Add journey has no roll pupil — the learner-details page IS the pupil.
    // Minting SelectedPupil here keeps every downstream consumer (summary {pupilName}
    // templating, drafts, BuildChangeRequestData, the amendment grid) working unchanged.
    //
    // AB#297780 SEAM: this POST's successful completion is the "learner details continued"
    // event the soft-match story will intercept — hook there, before the redirect that
    // follows this call, to branch into the match/query journeys.
    private void MintSyntheticPupilIfNeeded(Guid windowId, JourneyPage page)
    {
        if (!page.PupilFromAnswers) return;

        var refreshed = HttpContext.Session.GetRequestState(windowId);
        var pupil = AddPupilJourney.BuildPupil(refreshed, refreshed.SelectedPupil?.Id);
        var reference = refreshed.ReferenceNumber
            ?? journeyService.GenerateReference(refreshed.CheckingWindow?.CheckingWindowType);

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedPupil = pupil;
            s.SelectedPupilId = pupil.Id.ToString();
            s.SelectedPupilLabel = $"{pupil.Surname}, {pupil.Firstname}";
            s.ReferenceNumber = reference;
        });
    }

    private QuestionAnswer ReadFormAnswer(Question question)
    {
        var fieldName = FieldName(question.Id);
        return question.Type switch
        {
            QuestionType.Date => new QuestionAnswer
            {
                DateValue = new DateAnswer
                {
                    Day = int.TryParse(Request.Form[$"{fieldName}_day"], out var d) ? d : 0,
                    Month = int.TryParse(Request.Form[$"{fieldName}_month"], out var m) ? m : 0,
                    Year = int.TryParse(Request.Form[$"{fieldName}_year"], out var y) ? y : 0
                }
            },
            QuestionType.Autocomplete => new QuestionAnswer
            {
                TextValue = Request.Form[fieldName].FirstOrDefault()?.Trim(),
                CodeValue = Request.Form[$"{fieldName}_code"].FirstOrDefault()?.Trim() is { Length: > 0 } code ? code : null
            },
            _ => new QuestionAnswer { TextValue = Request.Form[fieldName].FirstOrDefault()?.Trim() }
        };
    }

    private void TrimHistoryTo(RequestState journey, Guid windowId, string pageId)
    {
        var idx = journey.QuestionHistory.IndexOf(pageId);
        if (idx >= 0)
            journey.QuestionHistory = journey.QuestionHistory.Take(idx + 1).ToList();
        else
            journey.QuestionHistory.Add(pageId);

        HttpContext.Session.SaveRequestState(windowId, s => s.QuestionHistory = journey.QuestionHistory);
    }
}
