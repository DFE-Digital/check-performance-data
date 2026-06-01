using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Web.FileStorage;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyController(
    IQuestionFlowService flowService,
    IJourneyValidationService journeyService,
    IFileStorageService fileStorageService,
    IRequestService requestService,
    IOptionVisibilityService optionVisibilityService,
    ICurrentUserService currentUserService,
    IWebHostEnvironment env) : Controller
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

        var nav = flowService.GetNavigationGuard(config, journey, pageId);
        if (nav is RedirectToJourneySummary) return RedirectToAction(nameof(Summary), new { windowId });
        if (nav is RedirectToJourneyPage { PageId: var navPageId })
            return RedirectToAction(nameof(Page), new { windowId, pageId = navPageId });

        var viewName = page.Type == PageType.EvidenceUpload ? "EvidenceUpload" : "Page";
        return View(viewName, BuildPageVm(windowId, page, journey.QuestionAnswers, journey, fromSummary, config));
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
        var pupilName = GetPupilName(journey);
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
                // Required answers are validated unconditionally; optional answers are
                // still format-checked (char limit, real date) when they have been filled in.
                if (!question.Optional || journeyService.IsAnswered(question, answer))
                {
                    var error = journeyService.ValidateAnswer(question, answer, Resolve(question.Title, pupilName));
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
            return View(invalidViewName, BuildPageVm(windowId, page, displayAnswers, journey, fromSummary, config, atLeastOne?.SummaryMessage));
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

        return View(BuildSummaryVm(windowId, journey, config));
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
            return View("Summary", BuildSummaryVm(windowId, journey, config,
                conflictError: "A request for this pupil has already been submitted. Select a different pupil."));
        }

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedWhatToChange = null;
            s.SelectedPupil = null;
            s.SelectedPupilId = null;
            s.SelectedPupilLabel = null;
            s.SelectedNextStep = null;
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

        // If posted from a question page, capture any unsaved non-file answers from the form
        if (pageId is not null)
        {
            var config = await GetConfigAsync(journey);
            var page = config is not null ? flowService.GetPage(config, pageId) : null;
            if (page is not null)
            {
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

        await requestService.SaveDraftAsync(windowId, journey);
        return RedirectToCheckYourData(windowId);
    }

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
        journey.CheckingWindow is not null &&
        journey.SelectedPupil is not null;

    private RedirectToActionResult RedirectToCheckYourData(Guid windowId) =>
        RedirectToAction("Index", "CheckYourPupilData", new { windowId });

    private Task<QuestionFlowConfig?> GetConfigAsync(RequestState journey) =>
        flowService.GetConfigAsync(journey.SelectedWhatToChange!.Value, journey.CheckingWindow!.CheckingWindowType);

    private static string GetPupilName(RequestState journey) =>
        journey.SelectedPupil is { } p ? $"{p.Firstname} {p.Surname}".Trim() : string.Empty;

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

    private static string Resolve(string template, string pupilName) =>
        template.Replace("{pupilName}", pupilName, StringComparison.OrdinalIgnoreCase);

    private PageViewModel BuildPageVm(Guid windowId, JourneyPage page,
        Dictionary<string, QuestionAnswer> answers, RequestState journey, bool fromSummary,
        QuestionFlowConfig? config = null, string? atLeastOneError = null)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(page.Id);
        var backPageId = historyIndex switch
        {
            -1 => journey.QuestionHistory.LastOrDefault(),
            0  => null,
            _  => journey.QuestionHistory[historyIndex - 1]
        };

        var pupilName = GetPupilName(journey);
        var isSingleQuestion = page.Questions.Count == 1;
        var uploadError = TempData["UploadError"] as string;

        string? contentKey = null;
        if (page.Type == PageType.Content && config is not null)
            contentKey = flowService.BuildContentKey(windowId, page, answers, journey, config);

        var conditionContext = BuildConditionContext(journey);

        var questionModels = page.Questions.Select(q =>
        {
            var error = ModelState.TryGetValue(q.Id, out var entry)
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
                Error = error,
                UploadError = uploadError,
                ResolvedTitle = Resolve(q.Title, pupilName) + (q.Optional ? " (Optional)" : ""),
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
            FromSummary = fromSummary,
            PupilName = pupilName,
            ContentKey = contentKey,
            UploadError = uploadError,
            AtLeastOneError = atLeastOneError,
            QuestionModels = questionModels
        };
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
            _ => new QuestionAnswer { TextValue = Request.Form[fieldName].FirstOrDefault()?.Trim() }
        };
    }

    private SummaryViewModel BuildSummaryVm(Guid windowId, RequestState journey, QuestionFlowConfig config, string? conflictError = null)
    {
        var pupilName = GetPupilName(journey);
        var rows = new List<SummaryRow>();
        var fileRows = new List<SummaryFileRow>();

        foreach (var pid in journey.QuestionHistory)
        {
            var p = flowService.GetPage(config, pid);
            if (p is null || p.Type == PageType.Content) continue;
            foreach (var q in p.Questions)
            {
                journey.QuestionAnswers.TryGetValue(q.Id, out var a);
                if (q.Type == QuestionType.FileUpload)
                {
                    if (a?.FileValues is { Count: > 0 } files)
                        fileRows.AddRange(files.Select(f => new SummaryFileRow(p, f.OriginalFileName, f.FileSizeBytes, f.PageCount, f.StoredFileName)));
                }
                else
                {
                    rows.Add(new SummaryRow(p, q, a, Resolve(q.SummaryTitle ?? q.Title, pupilName)));
                }
            }
        }

        var backPageId = journey.QuestionHistory.Last();
        var debugJson = env.IsDevelopment()
            ? System.Text.Json.JsonSerializer.Serialize(journey, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
            : null;

        return new SummaryViewModel
        {
            WindowId = windowId,
            WhatToChange = journey.SelectedWhatToChange!.Value,
            PupilName = pupilName,
            Rows = rows,
            FileRows = fileRows,
            BackPageId = backPageId,
            MaxEvidencePages = journeyService.MaxEvidencePages,
            DebugJson = debugJson,
            ConflictError = conflictError
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
