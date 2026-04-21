using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class CheckYourPupilDataController(ICurrentUserService currentUserService, IDfESignInApiClient apiClient, PortalDbContext dbContext) : Controller
{
    private const int PageSize = 10;

    private static readonly string[] DummyFirstnames = ["Alice", "Bob", "Charlie", "Diana", "Edward", "Fiona", "George", "Hannah", "Ian", "Julia",
        "Kevin", "Laura", "Michael", "Nina", "Oscar", "Paula", "Quinn", "Rachel", "Steven", "Tina"];

    private static readonly string[] DummySurnames = ["Smith", "Jones", "Williams", "Taylor", "Brown", "Davies", "Evans", "Wilson", "Thomas", "Roberts",
        "Johnson", "Lewis", "Walker", "Robinson", "Wood", "Thompson", "White", "Watson", "Jackson", "Harris"];

    private static readonly IReadOnlyList<PupilRow> DummyPupils = Enumerable.Range(1, 35).Select(i => new PupilRow
    {
        Firstname = DummyFirstnames[(i - 1) % DummyFirstnames.Length],
        Surname = DummySurnames[(i - 1) % DummySurnames.Length],
        DateOfBirth = new DateOnly(2010, (i % 12) + 1, (i % 28) + 1).ToString("dd/MM/yyyy"),
        Pincl = 100000 + i
    }).ToList();

    private static readonly IReadOnlyList<AssessmentResultRow> DummyResults = Enumerable.Range(1, 23).Select(i => new AssessmentResultRow
    {
        Name = $"{DummyFirstnames[(i - 1) % DummyFirstnames.Length]} {DummySurnames[(i - 1) % DummySurnames.Length]}",
        Subject = (i % 3) switch { 0 => "Mathematics", 1 => "Reading", _ => "Writing" },
        Outcome = (i % 4) switch { 0 => "Greater depth", 1 => "Expected standard", 2 => "Working towards", _ => "Below expected standard" }
    }).ToList();

    [Route("CheckYourPupilData/{windowId}")]
    public IActionResult Index(string windowId, int pupilsPage = 0, int resultsPage = 0)
    {
        var pupilsTotalPages = TotalPages(DummyPupils.Count);
        var resultsTotalPages = TotalPages(DummyResults.Count);

        pupilsPage = Math.Clamp(pupilsPage, 0, pupilsTotalPages - 1);
        resultsPage = Math.Clamp(resultsPage, 0, resultsTotalPages - 1);

        var model = new CheckYourPupilDataViewModel
        {
            WindowId = windowId,
            Pupils = DummyPupils.Skip(pupilsPage * PageSize).Take(PageSize).ToList(),
            PupilsPage = pupilsPage,
            PupilsTotalPages = pupilsTotalPages,
            Results = DummyResults.Skip(resultsPage * PageSize).Take(PageSize).ToList(),
            ResultsPage = resultsPage,
            ResultsTotalPages = resultsTotalPages
        };

        return View(model);
    }

    private static int TotalPages(int count) => (int)Math.Ceiling(count / (double)PageSize);
}