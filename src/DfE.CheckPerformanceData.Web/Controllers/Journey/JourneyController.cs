using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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
    JourneyViewModelBuilder viewModelBuilder) : Controller
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
        return View(viewName, viewModelBuilder.BuildPageVm(windowId, page, journey.QuestionAnswers,
            journey, fromSummary, ModelState, config));
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
            return View("PupilSearch", viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config));
        }

        if (page.PupilKey == JourneyPage.MatchKey && selectedPupilId == journey.SelectedPupilId)
        {
            var validationMessage = page.ValidationFailure is not null
                ? JourneyTemplate.Resolve(page.ValidationFailure, JourneyViewModelBuilder.GetPupilName(journey))
                : "Select a different pupil to the first record";
            ModelState.AddModelError("selectedPupilId", validationMessage);
            return View("PupilSearch", viewModelBuilder.BuildPupilSearchVm(windowId, pageId, page, journey, config));
        }

        var pupil = await pupilDataService.GetPupilAsync(windowId, pupilId);

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
    public async Task<IActionResult> PagePost(Guid windowId, string pageId, bool fromSummary)
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

        foreach (var question in page.Questions)
        {
            if (question.Type == QuestionType.FileUpload)
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var existing);
                var files = existing?.FileValues ?? [];
                if (files.Count == 0 && !question.Optional)
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
                // visibleWhen is a render-only gate: radio values are not validated against
                // the visible-options set here. A user who hand-crafts a hidden option value
                // will be routed into that branch and their request will reach Zendesk staff,
                // who review all submissions before acting on them.
                // Required answers are validated unconditionally; optional answers are
                // still format-checked (char limit, real date) when they have been filled in.
                if (!question.Optional || journeyService.IsAnswered(question, answer))
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
            var displayAnswers = journey.QuestionAnswers
                .Concat(newAnswers)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);
            var invalidViewName = page.Type == PageType.EvidenceUpload ? "EvidenceUpload" : "Page";
            return View(invalidViewName, viewModelBuilder.BuildPageVm(windowId, page, displayAnswers,
                journey, fromSummary, ModelState, config,
                uploadError: TempData["UploadError"] as string,
                atLeastOneError: atLeastOne?.SummaryMessage));
        }

        if (fromSummary)
        {
            var oldNextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);

            foreach (var (qId, answer) in newAnswers)
                journey.QuestionAnswers[qId] = answer;

            HttpContext.Session.SaveRequestState(windowId, s => s.QuestionAnswers = journey.QuestionAnswers);

            var newNextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);

            if (newNextId == oldNextId)
                return RedirectToAction(nameof(Summary), new { windowId });

            TrimHistoryTo(journey, windowId, pageId);
            if (newNextId is null)
                return RedirectToAction(nameof(Summary), new { windowId });

            return RedirectToAction(nameof(Page), new { windowId, pageId = newNextId });
        }

        foreach (var (qId, answer) in newAnswers)
            journey.QuestionAnswers[qId] = answer;

        if (!journey.QuestionHistory.Contains(pageId))
            journey.QuestionHistory.Add(pageId);

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.QuestionAnswers = journey.QuestionAnswers;
            s.QuestionHistory = journey.QuestionHistory;
        });

        var nextId = flowService.GetNextPageId(config, pageId, journey.QuestionAnswers);
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

        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];

        if (fileUpload is null || fileUpload.Length == 0)
        {
            TempData["UploadError"] = "Select a file to upload";
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        if (fileUpload.Length > MaxUploadBytes)
        {
            TempData["UploadError"] = $"'{fileUpload.FileName}' must be 10 MB or less";
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        using var ms = new MemoryStream();
        await fileUpload.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var pageCount = PdfPageCounter.GetPageCount(bytes);
        if (pageCount is null)
        {
            TempData["UploadError"] = $"'{fileUpload.FileName}' could not be read as a PDF. Check the file and try again.";
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        var uploadError = journeyService.ValidateFileUpload(fileUpload.FileName, pageCount.Value, currentFiles);
        if (uploadError is not null)
        {
            TempData["UploadError"] = uploadError;
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        var storedName = await fileStorageService.SaveAsync(windowId, bytes);
        currentFiles.Add(new FileAnswer
        {
            StoredFileName = storedName,
            OriginalFileName = fileUpload.FileName,
            PageCount = pageCount.Value,
            FileSizeBytes = bytes.LongLength
        });

        HttpContext.Session.SaveRequestState(windowId, s =>
            s.QuestionAnswers[questionId] = new QuestionAnswer { FileValues = currentFiles });

        return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
    }

    // ── File remove (POST) ─────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/page/{pageId}/question/{questionId}/remove")]
    public async Task<IActionResult> RemoveFile(Guid windowId, string pageId, string questionId,
        bool fromSummary, string storedFileName)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (!IsSessionReady(journey)) return RedirectToCheckYourData(windowId);

        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];

        if (currentFiles.All(f => f.StoredFileName != storedFileName))
            return BadRequest();

        currentFiles.RemoveAll(f => f.StoredFileName == storedFileName);

        await fileStorageService.DeleteAsync(windowId, storedFileName);

        HttpContext.Session.SaveRequestState(windowId, s =>
            s.QuestionAnswers[questionId] = new QuestionAnswer { FileValues = currentFiles });

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

        return View(viewModelBuilder.BuildSummaryVm(windowId, journey, config));
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
        catch (DuplicateRequestException)
        {
            var config = await GetConfigAsync(journey);
            if (config is null) return RedirectToCheckYourData(windowId);
            return View("Summary", viewModelBuilder.BuildSummaryVm(windowId, journey, config,
                conflictError: "A request for this pupil has already been submitted. Select a different pupil."));
        }

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedWhatToChange = null;
            s.SelectedPupil = null;
            s.SelectedPupilId = null;
            s.SelectedPupilLabel = null;
            s.SelectedNextStep = null;
            s.MatchedPupil = null;
            s.MatchedPupilId = null;
            s.MatchedPupilLabel = null;
            s.QuestionAnswers = new();
            s.QuestionHistory = new();
            // ReferenceNumber and CheckingWindow preserved for the Confirmation page
        });

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
        HttpContext.Session.ClearRequestState(windowId);
        return RedirectToAction("Index", "AmendmentRequests", new { windowId });
    }

    private RequestStatus DetermineStatus(string? pageId, JourneyPage? page, RequestState journey)
    {
        if (pageId is null) return RequestStatus.ReadyToSubmit;
        if (page?.Type != PageType.EvidenceUpload) return RequestStatus.InProgress;
        var pupilName = JourneyViewModelBuilder.GetPupilName(journey);
        return IsEvidencePageValid(page, journey, pupilName) ? RequestStatus.ReadyToSubmit : RequestStatus.InProgress;
    }

    private bool IsEvidencePageValid(JourneyPage page, RequestState journey, string pupilName) =>
        journeyService.ValidateEvidencePage(page, journey, pupilName) is null;

    // ── Confirmation ───────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/confirmation")]
    public IActionResult Confirmation(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);

        if (journey.ReferenceNumber is null || journey.CheckingWindow is null)
            return RedirectToCheckYourData(windowId);

        var window = journey.CheckingWindow;
        return View(new ConfirmationViewModel
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber,
            WindowCloseLabel = $"{window.EndDate.ToString("htt").ToLower()} on {window.EndDate:dddd d MMMM yyyy}"
        });
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
