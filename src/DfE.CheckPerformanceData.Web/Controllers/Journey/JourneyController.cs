using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Web.FileStorage;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyController(
    IQuestionFlowService flowService,
    IJourneyService journeyService,
    IFileStorageService fileStorageService,
    IRequestBlobClient requestBlobClient) : Controller
{
    internal static string FieldName(string questionId) => $"q_{questionId.Replace("-", "_")}";

    // ── Page (GET) ──────────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/page/{pageId}")]
    public IActionResult Page(Guid windowId, string pageId, bool fromSummary = false)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var config = GetConfigOrNotFound(journey);
        if (config is null) return NotFound();

        var page = flowService.GetPage(config, pageId);
        return View("Page", BuildPageVm(windowId, page, journey.QuestionAnswers, journey, fromSummary, config));
    }

    // ── Page (POST — Continue) ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/page/{pageId}")]
    public IActionResult PagePost(Guid windowId, string pageId, bool fromSummary)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var config = GetConfigOrNotFound(journey);
        if (config is null) return NotFound();

        var page = flowService.GetPage(config, pageId);
        var newAnswers = new Dictionary<string, QuestionAnswer>();
        var pupilName = GetPupilName(journey);
        var isValid = true;

        foreach (var question in page.Questions)
        {
            if (question.Type == QuestionType.FileUpload)
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var existing);
                var files = existing?.FileValues ?? [];
                if (files.Count == 0)
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
                var error = journeyService.ValidateAnswer(question, answer, Resolve(question.Title, pupilName));
                if (error is not null)
                {
                    ModelState.AddModelError(question.Id, error);
                    isValid = false;
                }
                newAnswers[question.Id] = answer;
            }
        }

        if (!isValid)
        {
            var displayAnswers = journey.QuestionAnswers
                .Concat(newAnswers)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);
            return View("Page", BuildPageVm(windowId, page, displayAnswers, journey, fromSummary, config));
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
        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];

        if (fileUpload is null || fileUpload.Length == 0)
        {
            TempData["UploadError"] = "Select a file to upload";
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
        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];
        currentFiles.RemoveAll(f => f.StoredFileName == storedFileName);

        await fileStorageService.DeleteAsync(windowId, storedFileName);

        HttpContext.Session.SaveRequestState(windowId, s =>
            s.QuestionAnswers[questionId] = new QuestionAnswer { FileValues = currentFiles });

        return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
    }

    // ── Summary ────────────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/summary")]
    public IActionResult Summary(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var config = GetConfigOrNotFound(journey);
        if (config is null) return NotFound();

        var pupilName = GetPupilName(journey);

        var rows = journey.QuestionHistory
            .SelectMany(pid =>
            {
                var p = flowService.GetPage(config, pid);
                if (p.Type == PageType.Content) return Enumerable.Empty<SummaryRow>();
                return p.Questions.Select(q =>
                {
                    journey.QuestionAnswers.TryGetValue(q.Id, out var a);
                    return new SummaryRow(p, q, a, Resolve(q.Title, pupilName));
                });
            })
            .ToList();

        var backPageId = journey.QuestionHistory.LastOrDefault() ?? config.FirstPageId;

        var debugJson = System.Text.Json.JsonSerializer.Serialize(journey,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        return View(new SummaryViewModel { WindowId = windowId, Rows = rows, BackPageId = backPageId, DebugJson = debugJson });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/summary")]
    public async Task<IActionResult> SummaryConfirm(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var config = GetConfigOrNotFound(journey);

        if (config is not null && journey.SelectedPupil is { Cypmd_Id: not null } pupil)
        {
            var context = new JourneySubmissionContext
            {
                WindowId = windowId,
                ReferenceNumber = journey.ReferenceNumber ?? string.Empty,
                WhatToChange = journey.SelectedWhatToChange!.Value,
                Pupil = pupil,
                CheckingWindow = journey.CheckingWindow!,
                Answers = journey.QuestionAnswers,
                History = journey.QuestionHistory
            };
            var document = journeyService.BuildRequestDocument(context, config);
            await requestBlobClient.SaveRequestAsync(windowId, document);
        }

        return RedirectToAction(nameof(Confirmation), new { windowId });
    }

    // ── Confirmation ───────────────────────────────────────────────────────

    [Route("/Journey/{windowId}/confirmation")]
    public IActionResult Confirmation(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var window = journey.CheckingWindow!;
        return View(new ConfirmationViewModel
        {
            WindowId = windowId,
            ReferenceNumber = journey.ReferenceNumber,
            WindowCloseLabel = $"{window.EndDate.ToString("htt").ToLower()} on {window.EndDate:dddd d MMMM yyyy}"
        });
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private QuestionFlowConfig? GetConfigOrNotFound(RequestState journey)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null) return null;
        return flowService.GetConfig(journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
    }

    private static string GetPupilName(RequestState journey) =>
        journey.SelectedPupil is { } p ? $"{p.Firstname} {p.Surname}".Trim() : string.Empty;

    private static string Resolve(string template, string pupilName) =>
        template.Replace("{pupilName}", pupilName, StringComparison.OrdinalIgnoreCase);

    private PageViewModel BuildPageVm(Guid windowId, JourneyPage page,
        Dictionary<string, QuestionAnswer> answers, RequestState journey, bool fromSummary,
        QuestionFlowConfig? config = null)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(page.Id);
        var backPageId = historyIndex switch
        {
            -1 => journey.QuestionHistory.LastOrDefault(),
            0  => null,
            _  => journey.QuestionHistory[historyIndex - 1]
        };

        var pupilName = GetPupilName(journey);

        string? contentKey = null;
        if (page.Type == PageType.Content && config is not null)
            contentKey = BuildContentKey(windowId, page, answers, journey, config);

        return new PageViewModel
        {
            WindowId = windowId,
            Page = page,
            Answers = answers,
            BackPageId = backPageId,
            FromSummary = fromSummary,
            PupilName = pupilName,
            ContentKey = contentKey
        };
    }

    private static string BuildContentKey(Guid windowId, JourneyPage page,
        Dictionary<string, QuestionAnswer> answers, RequestState journey, QuestionFlowConfig config)
    {
        var whatToChange = journey.SelectedWhatToChange?.ToString().ToLower() ?? "unknown";

        var pageIndex = journey.QuestionHistory.IndexOf(page.Id);
        IEnumerable<string> historyBeforePage = pageIndex >= 0
            ? journey.QuestionHistory.Take(pageIndex)
            : journey.QuestionHistory;

        var radioValues = historyBeforePage
            .SelectMany(pid =>
            {
                var p = config.Pages.FirstOrDefault(p => p.Id == pid);
                if (p is null) return Enumerable.Empty<string>();
                return p.Questions
                    .Where(q => q.Type == QuestionType.Radio && q.ContentKey)
                    .Select(q => answers.TryGetValue(q.Id, out var a) ? a.TextValue : null)
                    .Where(v => v is not null)
                    .Select(v => v!);
            });

        return string.Join("-", new[] { "journey", windowId.ToString(), whatToChange }.Concat(radioValues));
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
