using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WindowAdminController(
    IWindowService windowService,
    IWindowStatusService windowStatusService
    ) : Controller
{
    [HttpGet("admin/windows")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        PageResult? pageResult = await windowService.GetAllDataAsync(cancellationToken);
        List<CheckingWindowDto> windows = pageResult?.Windows ?? [];

        // Asked once for the whole list rather than per row, so every row on the page is answered
        // against the same instant.
        HashSet<Guid> openWindowIds = windowStatusService.OpenWindows(windows).Select(w => w.Id).ToHashSet();

        List<WindowListItem> windowListItems = windows
            .Select(w => new WindowListItem
            {
                Id = w.Id,
                Name = w.Title,
                IsOpen = openWindowIds.Contains(w.Id),
                IsPublished = true
            })
            .ToList();
        WindowViewModel vm = new WindowViewModel(windowListItems);

        return View( vm );
    }
}