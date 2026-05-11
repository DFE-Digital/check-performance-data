using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.QuestionFlow;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyController(
    IQuestionFlowService flowService,
    IWebHostEnvironment env,
    IRequestBlobClient requestBlobClient,
    ICurrentUserService currentUserService) : Controller
{
    private const int MaxTotalPages = 6;

    // Converts a question ID to a safe HTML field name, e.g. "date-of-death" → "q_date_of_death"
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
                var error = GetAnswerError(question, answer);
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
            // Merge submitted values over session so re-rendered fields are pre-filled
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

        int pageCount;
        try
        {
            using var doc = PdfDocument.Open(bytes);
            pageCount = doc.NumberOfPages;
        }
        catch
        {
            TempData["UploadError"] = $"'{fileUpload.FileName}' could not be read as a PDF. Check the file and try again.";
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        var currentTotal = currentFiles.Sum(f => f.PageCount);
        if (currentTotal + pageCount > MaxTotalPages)
        {
            TempData["UploadError"] = $"'{fileUpload.FileName}' has {pageCount} {(pageCount == 1 ? "page" : "pages")}. " +
                $"Adding it would bring the total to {currentTotal + pageCount} pages, " +
                $"which exceeds the {MaxTotalPages}-page limit.";
            return RedirectToAction(nameof(Page), new { windowId, pageId, fromSummary });
        }

        var uploadsPath = Path.Combine(env.ContentRootPath, "Uploads");
        Directory.CreateDirectory(uploadsPath);
        var storedName = Guid.NewGuid().ToString();
        await System.IO.File.WriteAllBytesAsync(Path.Combine(uploadsPath, storedName), bytes);

        currentFiles.Add(new FileAnswer
        {
            StoredFileName = storedName,
            OriginalFileName = fileUpload.FileName,
            PageCount = pageCount,
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
    public IActionResult RemoveFile(Guid windowId, string pageId, string questionId,
        bool fromSummary, string storedFileName)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        journey.QuestionAnswers.TryGetValue(questionId, out var existing);
        var currentFiles = existing?.FileValues?.ToList() ?? [];
        currentFiles.RemoveAll(f => f.StoredFileName == storedFileName);

        var filePath = Path.Combine(env.ContentRootPath, "Uploads", storedFileName);
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

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

        var pupil = journey.SelectedPupil;
        var pupilName = pupil is not null ? $"{pupil.Firstname} {pupil.Surname}".Trim() : string.Empty;

        string Resolve(string template) =>
            template.Replace("{pupilName}", pupilName, StringComparison.OrdinalIgnoreCase);

        var rows = journey.QuestionHistory
            .SelectMany(pid =>
            {
                var p = flowService.GetPage(config, pid);
                if (p.Type == PageType.Content) return Enumerable.Empty<SummaryRow>();
                return p.Questions.Select(q =>
                {
                    journey.QuestionAnswers.TryGetValue(q.Id, out var a);
                    return new SummaryRow(p, q, a, Resolve(q.Title));
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

        if (config is not null && journey.SelectedPupil?.Cypmd_Id is not null)
        {
            var document = BuildRequestDocument(windowId, journey, config);
            await requestBlobClient.SaveRequestAsync(windowId, document);
        }

        return RedirectToAction(nameof(Confirmation), new { windowId });
    }

    private RequestDocument BuildRequestDocument(Guid windowId, RequestState journey, QuestionFlowConfig config)
    {
        var pupil = journey.SelectedPupil!;
        var pupilName = $"{pupil.Firstname} {pupil.Surname}".Trim();

        string Resolve(string template) =>
            template.Replace("{pupilName}", pupilName, StringComparison.OrdinalIgnoreCase);

        var answers = journey.QuestionHistory
            .SelectMany(pid =>
            {
                var page = flowService.GetPage(config, pid);
                if (page.Type == PageType.Content) return Enumerable.Empty<AnswerRecord>();
                return page.Questions.Select(q =>
                {
                    journey.QuestionAnswers.TryGetValue(q.Id, out var ans);
                    return BuildAnswerRecord(q, ans, Resolve);
                });
            })
            .ToList();

        return new RequestDocument
        {
            Status = RequestStatus.Submitted,
            ReferenceNumber = journey.ReferenceNumber ?? string.Empty,
            SubmittedAt = DateTime.UtcNow,
            SubmittedBy = new UserDetails
            {
                UserId = currentUserService.UserId,
                DisplayName = currentUserService.DisplayName
            },
            CheckingWindowId = windowId,
            CheckingWindowType = journey.CheckingWindowType?.ToString() ?? string.Empty,
            WhatToChange = journey.SelectedWhatToChange?.ToString() ?? string.Empty,
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
                Age = pupil.Age
            },
            Answers = answers
        };
    }

    private static AnswerRecord BuildAnswerRecord(Question question, QuestionAnswer? answer, Func<string, string> resolve)
    {
        var title = resolve(question.Title);

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

    [Route("/Journey/{windowId}/confirmation")]
    public IActionResult Confirmation(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        return View(new ConfirmationViewModel { WindowId = windowId, ReferenceNumber = journey.ReferenceNumber });
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private QuestionFlowConfig? GetConfigOrNotFound(RequestState journey)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindowType is null) return null;
        return flowService.GetConfig(journey.SelectedWhatToChange.Value, journey.CheckingWindowType.Value);
    }

    private PageViewModel BuildPageVm(Guid windowId, JourneyPage page,
        Dictionary<string, QuestionAnswer> answers, RequestState journey, bool fromSummary,
        QuestionFlowConfig? config = null)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(page.Id);
        var backPageId = historyIndex switch
        {
            -1 => journey.QuestionHistory.LastOrDefault(),  // first visit — came from last page in history
            0  => null,                                      // first page in journey — back goes to pupil search
            _  => journey.QuestionHistory[historyIndex - 1]
        };

        var pupil = journey.SelectedPupil;
        var pupilName = pupil is not null
            ? $"{pupil.Firstname} {pupil.Surname}".Trim()
            : string.Empty;

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

    private static string? GetAnswerError(Question question, QuestionAnswer answer) =>
        question.Type switch
        {
            QuestionType.Date when answer.DateValue is not { Day: > 0, Month: > 0, Year: > 0 }
                => $"{question.Title} is required",
            QuestionType.TextArea when string.IsNullOrWhiteSpace(answer.TextValue)
                => $"{question.Title} is required",
            QuestionType.TextArea when question.CharacterLimit.HasValue && answer.TextValue!.Length > question.CharacterLimit.Value
                => $"{question.Title} must be {question.CharacterLimit} characters or less",
            QuestionType.Date => null,
            _ when string.IsNullOrWhiteSpace(answer.TextValue)
                => $"{question.Title} is required",
            _ => null
        };

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
