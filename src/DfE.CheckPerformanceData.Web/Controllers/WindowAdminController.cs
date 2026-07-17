using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WindowAdminController(
    IWindowService windowService
    ) : Controller
{
    [HttpGet("admin/windows")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        PageResult? pageResult = await windowService.GetAllDataAsync(cancellationToken);
        List<WindowListItem> windowListItems = pageResult?.Windows.Select(wli => new WindowListItem() {Id = wli.Id, Name = wli.Title, IsOpen = true, IsPublished = true}).ToList()!;
        WindowViewModel vm = new WindowViewModel(windowListItems!);

        return View( vm );
    }
}