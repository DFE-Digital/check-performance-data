using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
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
    IOriginCountryLanguageCapture originCountryLanguageCapture) : Controller
{
    internal static string FieldName(string questionId) => $"q_{questionId.Replace("-", "_")}";

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

        var nav = flowService.GetNavigationGuard(config, journey, pageId);
        if (nav is RedirectToJourneySummary) return RedirectToAction(nameof(Summary), new { windowId });
        if (nav is RedirectToJourneyPage { PageId: var navPageId })
            return RedirectToAction(nameof(Page), new { windowId, pageId = navPageId });

        var viewName = page.Type == PageType.EvidenceUpload ? "EvidenceUpload" : "Page";
        // Surface an upload error stashed by UploadFile before its PRG redirect here — otherwise
        // a rejected upload (e.g. a non-PDF) would silently show no validation message.
        return View(viewName, viewModelBuilder.BuildPageVm(windowId, page, journey.QuestionAnswers,
            journey, fromSummary, ModelState, config,
            uploadError: TempData["UploadError"] as string));
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
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
            });
            return View("PupilSearch", viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config));
        }

        var pupil = await pupilDataService.GetPupilAsync(windowId, pupilId);

        if (page.PupilKey != JourneyPage.MatchKey)
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
            var reference = journeyService.GenerateReference(journey.CheckingWindow?.CheckingWindowType);
            HttpContext.Session.SaveRequestState(windowId, s =>
            {
                s.SelectedPupilId = selectedPupilId;
                s.SelectedPupilLabel = selectedPupilLabel;
                s.SelectedPupil = pupil;
                s.ReferenceNumber = reference;
                s.QuestionAnswers = new Dictionary<string, QuestionAnswer>();
                s.QuestionHistory = [pageId];
                s.MatchedPupil = null;
                s.MatchedPupilId = null;
                s.MatchedPupilLabel = null;
            });
        }

        if (page.NextPageId is null)
            return RedirectToAction(nameof(Summary), new { windowId });

        var nextPage = flowService.GetPage(config, page.NextPageId);
        return nextPage?.Type == PageType.PupilSearch
            ? RedirectToAction(nameof(PupilSearchPage), new { windowId, pageId = page.NextPageId })
            : RedirectToAction(nameof(Page), new { windowId, pageId = page.NextPageId });
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
                    var error = journeyService.ValidateAnswer(question, answer, JourneyTemplate.Resolve(question.Title, pupilName), resolvedValidationFailure);
                    if (error is not null)
                    {
                        ModelState.AddModelError(question.Id, error);
                        isValid = false;
                    }
                }
                newAnswers[question.Id] = answer;
            }
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
            foreach (var q in page.Questions)
            {
                if (!ModelState.TryGetValue(q.Id, out var entry) || entry.Errors.Count == 0) continue;
                if (q.Type == QuestionType.FileUpload) { codes.Add(ValidationErrorCoding.FileRequired); continue; }
                newAnswers.TryGetValue(q.Id, out var ans);
                var answered = ans is not null && journeyService.IsAnswered(q, ans);
                codes.Add(ValidationErrorCoding.ForQuestion(q, answered));
            }
            if (atLeastOne is not null) codes.Add(ValidationErrorCoding.AtLeastOne);
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = ModelState.ErrorCount,
                ErrorCodes = codes,
                WhatToChange = journey.SelectedWhatToChange?.ToString(),
                FromSummary = fromSummary,
            });

            var displayAnswers = journey.QuestionAnswers
                .Concat(newAnswers)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);
            var invalidViewName = page.Type == PageType.EvidenceUpload ? "EvidenceUpload" : "Page";
            return View(invalidViewName, viewModelBuilder.BuildPageVm(windowId, page, displayAnswers,
                journey, fromSummary, ModelState, config,
                uploadError: pendingUploadError ?? TempData["UploadError"] as string,
                atLeastOneError: atLeastOne?.SummaryMessage));
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

        var fromBulk = HttpContext.Session.IsBulkEditMode(windowId);
        var fromEdit = HttpContext.Session.IsSingleEditMode(windowId);
        return View(viewModelBuilder.BuildSummaryVm(windowId, journey, config, fromBulk: fromBulk, fromEdit: fromEdit));
    }

    [Route("/Journey/{windowId}/evidence/{storedFileName}")]
    public async Task<IActionResult> DownloadEvidence(Guid windowId, string storedFileName)
    {
        if (!Guid.TryParse(storedFileName, out _)) return NotFound();

        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return NotFound();

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
                foreach (var question in page.Questions.Where(q => q.Type != QuestionType.FileUpload))
                {
                    var answer = ReadFormAnswer(question);
                    if (!string.IsNullOrWhiteSpace(answer.TextValue))
                        HttpContext.Session.SaveRequestState(windowId,
                            s => s.QuestionAnswers[question.Id] = answer);
                }
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

    private static bool IsSessionReady(RequestState journey) =>
        journey.SelectedWhatToChange is not null &&
        journey.CheckingWindow is not null;

    private RedirectToActionResult RedirectToCheckYourData(Guid windowId) =>
        RedirectToAction("Index", "CheckYourPupilData", new { windowId });

    private RedirectToActionResult RedirectToJourneyAction(QuestionFlowConfig config, Guid windowId, string pageId)
    {
        var target = flowService.GetPage(config, pageId);
        return target?.Type == PageType.PupilSearch
            ? RedirectToAction(nameof(PupilSearchPage), new { windowId, pageId })
            : RedirectToAction(nameof(Page), new { windowId, pageId });
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
