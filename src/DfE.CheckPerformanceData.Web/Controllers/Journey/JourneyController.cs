using DfE.CheckPerformanceData.Web.QuestionFlow;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class JourneyController(IQuestionFlowService flowService, IWebHostEnvironment env) : Controller
{
    [Route("/Journey/{windowId}/question/{questionId}")]
    public IActionResult Question(Guid windowId, string questionId, bool fromSummary = false)
    {
        var journey = HttpContext.Session.GetJourneyState(windowId);
        var config = GetConfigOrNotFound(journey);
        if (config is null) return NotFound();

        var question = flowService.GetQuestion(config, questionId);
        journey.QuestionAnswers.TryGetValue(questionId, out var existingAnswer);

        return View("Question", BuildQuestionVm(windowId, question, existingAnswer, journey, fromSummary));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/question/{questionId}")]
    public async Task<IActionResult> QuestionPost(Guid windowId, string questionId, bool fromSummary,
        string? textValue, int? dateDay, int? dateMonth, int? dateYear, IFormFile? fileUpload)
    {
        var journey = HttpContext.Session.GetJourneyState(windowId);
        var config = GetConfigOrNotFound(journey);
        if (config is null) return NotFound();

        var question = flowService.GetQuestion(config, questionId);

        var answer = BuildAnswer(question, textValue, dateDay, dateMonth, dateYear, fileUpload);

        if (!ValidateAnswer(question, answer, fileUpload))
        {
            ModelState.AddModelError(string.Empty, $"{question.Title} is required");
            return View("Question", BuildQuestionVm(windowId, question, answer, journey));
        }

        if (question.Type == QuestionType.FileUpload && fileUpload is not null)
            answer = await SaveFileAsync(answer, fileUpload);

        if (fromSummary)
        {
            journey.QuestionAnswers.TryGetValue(questionId, out var oldAnswer);
            var oldNextId = flowService.GetNextQuestionId(config, questionId, oldAnswer);

            journey.QuestionAnswers[questionId] = answer;
            HttpContext.Session.SaveJourneyState(windowId, s => s.QuestionAnswers = journey.QuestionAnswers);

            var newNextId = flowService.GetNextQuestionId(config, questionId, answer);

            if (newNextId == oldNextId)
                return RedirectToAction(nameof(Summary), new { windowId });

            // Branch changed — trim history to this question and continue forward
            TrimHistoryTo(journey, windowId, questionId);
            if (newNextId is null)
                return RedirectToAction(nameof(Summary), new { windowId });

            return RedirectToAction(nameof(Question), new { windowId, questionId = newNextId });
        }

        // Normal flow
        journey.QuestionAnswers[questionId] = answer;
        if (!journey.QuestionHistory.Contains(questionId))
            journey.QuestionHistory.Add(questionId);

        HttpContext.Session.SaveJourneyState(windowId, s =>
        {
            s.QuestionAnswers = journey.QuestionAnswers;
            s.QuestionHistory = journey.QuestionHistory;
        });

        var nextId = flowService.GetNextQuestionId(config, questionId, answer);
        if (nextId is null)
            return RedirectToAction(nameof(Summary), new { windowId });

        return RedirectToAction(nameof(Question), new { windowId, questionId = nextId });
    }

    [Route("/Journey/{windowId}/summary")]
    public IActionResult Summary(Guid windowId)
    {
        var journey = HttpContext.Session.GetJourneyState(windowId);
        var config = GetConfigOrNotFound(journey);
        if (config is null) return NotFound();

        var rows = journey.QuestionHistory
            .Select(id =>
            {
                var q = flowService.GetQuestion(config, id);
                journey.QuestionAnswers.TryGetValue(id, out var a);
                return new SummaryRow(q, a);
            })
            .ToList();

        var backQuestionId = journey.QuestionHistory.LastOrDefault() ?? config.FirstQuestionId;

        return View(new SummaryViewModel
        {
            WindowId = windowId,
            Rows = rows,
            BackQuestionId = backQuestionId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Journey/{windowId}/summary")]
    public IActionResult SummaryConfirm(Guid windowId) =>
        RedirectToAction(nameof(Confirmation), new { windowId });

    [Route("/Journey/{windowId}/confirmation")]
    public IActionResult Confirmation(Guid windowId) => View(new ConfirmationViewModel { WindowId = windowId });

    // --- helpers ---

    private QuestionFlowConfig? GetConfigOrNotFound(JourneyState journey)
    {
        if (journey.SelectedWhatToChange is null || journey.KeyStage is null) return null;
        return flowService.GetConfig(journey.SelectedWhatToChange.Value, journey.KeyStage.Value);
    }

    private QuestionViewModel BuildQuestionVm(Guid windowId, Question question, QuestionAnswer? answer, JourneyState journey, bool fromSummary = false)
    {
        var historyIndex = journey.QuestionHistory.IndexOf(question.Id);
        var backQuestionId = historyIndex > 0 ? journey.QuestionHistory[historyIndex - 1] : null;

        return new QuestionViewModel
        {
            WindowId = windowId,
            Question = question,
            ExistingAnswer = answer,
            BackQuestionId = backQuestionId,
            FromSummary = fromSummary
        };
    }

    private static QuestionAnswer BuildAnswer(Question question, string? textValue,
        int? dateDay, int? dateMonth, int? dateYear, IFormFile? fileUpload)
    {
        return question.Type switch
        {
            QuestionType.Date => new QuestionAnswer
            {
                DateValue = (dateDay.HasValue || dateMonth.HasValue || dateYear.HasValue)
                    ? new DateAnswer { Day = dateDay ?? 0, Month = dateMonth ?? 0, Year = dateYear ?? 0 }
                    : null
            },
            QuestionType.FileUpload => new QuestionAnswer(),  // file saved separately
            _ => new QuestionAnswer { TextValue = textValue?.Trim() }
        };
    }

    private static bool ValidateAnswer(Question question, QuestionAnswer answer, IFormFile? fileUpload)
    {
        return question.Type switch
        {
            QuestionType.Date => answer.DateValue is { Day: > 0, Month: > 0, Year: > 0 },
            QuestionType.FileUpload => fileUpload is { Length: > 0 },
            _ => !string.IsNullOrWhiteSpace(answer.TextValue)
        };
    }

    private async Task<QuestionAnswer> SaveFileAsync(QuestionAnswer answer, IFormFile fileUpload)
    {
        var uploadsPath = Path.Combine(env.ContentRootPath, "Uploads");
        Directory.CreateDirectory(uploadsPath);

        var storedName = Guid.NewGuid().ToString();
        var filePath = Path.Combine(uploadsPath, storedName);

        await using var stream = System.IO.File.Create(filePath);
        await fileUpload.CopyToAsync(stream);

        return new QuestionAnswer
        {
            FileValue = new FileAnswer
            {
                StoredFileName = storedName,
                OriginalFileName = fileUpload.FileName
            }
        };
    }

    private void TrimHistoryTo(JourneyState journey, Guid windowId, string questionId)
    {
        var idx = journey.QuestionHistory.IndexOf(questionId);
        if (idx >= 0)
            journey.QuestionHistory = journey.QuestionHistory.Take(idx + 1).ToList();
        else
            journey.QuestionHistory.Add(questionId);

        HttpContext.Session.SaveJourneyState(windowId, s => s.QuestionHistory = journey.QuestionHistory);
    }
}
