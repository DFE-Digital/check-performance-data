using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;
using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// #937: adds a school to, and removes a school from, the list of the schools in a window that
/// see sample data. Each change has a confirmation page. The change has an effect only after the
/// next validation of each exercise.
/// </summary>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class SampleSchoolsController(
    ISampleSchoolService sampleSchools,
    IWindowService windowService,
    ICurrentUserService currentUser,
    EstablishmentDirectory establishments) : Controller
{
    /// <summary>Carries the outcome sentence to the notification banner on the summary page.</summary>
    public const string TempDataKey = "SampleSchoolOutcome";

    private const string AlreadyListed = "This window already uses sample data for this school";

    private const string AddView = "~/Views/WindowAdmin/AddSampleSchool.cshtml";
    private const string ConfirmAddView = "~/Views/WindowAdmin/ConfirmSampleSchool.cshtml";
    private const string RemoveView = "~/Views/WindowAdmin/RemoveSampleSchool.cshtml";

    [HttpGet("admin/windows/{id:guid}/sample-schools/add")]
    public async Task<IActionResult> Add(Guid id, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
            return NotFound();

        return View(AddView, AddModel(window, new SampleSchoolAddItem()));
    }

    /// <summary>
    /// The school search of the add page. It reads the same establishment directory as
    /// impersonation, and gives only the name, URN and LAESTAB of each school.
    /// </summary>
    [HttpGet("admin/windows/{id:guid}/sample-schools/search")]
    public IActionResult Search(Guid id, [FromQuery] string? query) =>
        Json(establishments.Search(query).Select(e => new { e.Name, e.Urn, e.Laestab }));

    /// <summary>Checks the values, then shows the confirmation page. Writes nothing.</summary>
    [HttpPost("admin/windows/{id:guid}/sample-schools/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(Guid id, SampleSchoolAddItem model, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
            return NotFound();

        var result = await sampleSchools.CheckAddAsync(id, model.Laestab, model.Urn, cancellationToken);
        if (result.Status == SampleSchoolAddStatus.WindowNotFound)
            return NotFound();
        if (result.Status != SampleSchoolAddStatus.Ok)
            return AddWithErrors(window, model, result);

        return View(ConfirmAddView, new SampleSchoolConfirmItem
        {
            WindowId = id,
            WindowTitle = window.Title,
            Laestab = result.Input.Laestab!,
            LaestabDisplay = LaestabList.Display(result.Input.Laestab!),
            Urn = result.Input.Urn!,
            SchoolName = establishments.Find(result.Input.Laestab!, result.Input.Urn!)?.Name,
            PostUrl = $"{BaseUrl(id)}/add/confirm",
            ChangeUrl = $"{BaseUrl(id)}/add",
            CancelUrl = SummaryUrl(id)
        });
    }

    [HttpPost("admin/windows/{id:guid}/sample-schools/add/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, SampleSchoolAddItem model, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
            return NotFound();

        // The service checks the values again: the hidden fields come from the browser.
        var result = await sampleSchools.AddAsync(id, model.Laestab, model.Urn, currentUser.DisplayName, cancellationToken);
        if (result.Status == SampleSchoolAddStatus.WindowNotFound)
            return NotFound();
        if (result.Status != SampleSchoolAddStatus.Ok)
            return AddWithErrors(window, model, result);

        TempData[TempDataKey] =
            $"School {LaestabList.Display(result.Input.Laestab!)} added. Validate each exercise again to give it sample data.";
        return Redirect(SummaryUrl(id));
    }

    [HttpGet("admin/windows/{id:guid}/sample-schools/{sampleSchoolId:guid}/remove")]
    public async Task<IActionResult> Remove(Guid id, Guid sampleSchoolId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var school = window is null ? null : await sampleSchools.FindAsync(id, sampleSchoolId, cancellationToken);
        if (window is null || school is null)
            return NotFound();

        return View(RemoveView, new SampleSchoolRemoveItem
        {
            WindowId = id,
            WindowTitle = window.Title,
            LaestabDisplay = LaestabList.Display(school.Laestab),
            Urn = school.Urn,
            PostUrl = $"{BaseUrl(id)}/{sampleSchoolId}/remove",
            CancelUrl = SummaryUrl(id)
        });
    }

    [HttpPost("admin/windows/{id:guid}/sample-schools/{sampleSchoolId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveConfirmed(Guid id, Guid sampleSchoolId, CancellationToken cancellationToken)
    {
        var school = await sampleSchools.FindAsync(id, sampleSchoolId, cancellationToken);
        if (school is null || !await sampleSchools.RemoveAsync(id, sampleSchoolId, cancellationToken))
            return NotFound();

        TempData[TempDataKey] =
            $"School {LaestabList.Display(school.Laestab)} removed. Validate each exercise again to give it its real data.";
        return Redirect(SummaryUrl(id));
    }

    private ViewResult AddWithErrors(CheckingWindowDto window, SampleSchoolAddItem model, SampleSchoolAddResult result)
    {
        if (result.Input.LaestabError is { } laestabError)
            ModelState.AddModelError(nameof(SampleSchoolAddItem.Laestab), laestabError);
        else if (result.Status == SampleSchoolAddStatus.AlreadyListed)
            ModelState.AddModelError(nameof(SampleSchoolAddItem.Laestab), AlreadyListed);
        if (result.Input.UrnError is { } urnError)
            ModelState.AddModelError(nameof(SampleSchoolAddItem.Urn), urnError);

        return View(AddView, AddModel(window, model));
    }

    private SampleSchoolAddItem AddModel(CheckingWindowDto window, SampleSchoolAddItem model) => new()
    {
        WindowId = window.Id,
        WindowTitle = window.Title,
        Laestab = model.Laestab,
        Urn = model.Urn,
        PostUrl = $"{BaseUrl(window.Id)}/add",
        SearchUrl = $"{BaseUrl(window.Id)}/search",
        CancelUrl = SummaryUrl(window.Id)
    };

    private static string BaseUrl(Guid id) => $"/admin/windows/{id}/sample-schools";

    private static string SummaryUrl(Guid id) => $"/admin/windows/summary/{id}";
}
