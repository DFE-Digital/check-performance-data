using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.HomeBanners;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Start-page banners admin (#566): list, create, edit, preview, versions/restore, delete and
// reorder. Thin: every rule (live/status, sanitising, versioning) lives in IHomeBannerService.
[RequireAdminSection(AdminNavKeys.HomeBanners)]
public sealed class HomeBannersController(IHomeBannerService banners, TimeProvider timeProvider) : Controller
{
    private const string ListUrl = "/admin/home-banners";

    [HttpGet("admin/home-banners")]
    public async Task<IActionResult> Index() =>
        View(new HomeBannersIndexViewModel
        {
            Banners = await banners.GetAllAsync(),
            Now = timeProvider.GetLocalNow().DateTime
        });

    [HttpGet("admin/home-banners/new")]
    public IActionResult New() => View("Edit", new HomeBannerFormModel());

    [HttpPost("admin/home-banners/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HomeBannerFormModel model)
    {
        if (ModelState.IsValid) model.ValidateDates(ModelState);
        if (!ModelState.IsValid) return View("Edit", model);

        await banners.CreateAsync(model.ToContent());
        return Redirect(ListUrl);
    }

    [HttpGet("admin/home-banners/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var banner = await banners.GetByIdAsync(id);
        return banner is null ? NotFound() : View("Edit", HomeBannerFormModel.From(banner));
    }

    [HttpPost("admin/home-banners/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, HomeBannerFormModel model)
    {
        model.Id = id;
        if (ModelState.IsValid) model.ValidateDates(ModelState);
        if (!ModelState.IsValid) return View("Edit", model);

        var updated = await banners.UpdateAsync(id, model.ToContent());
        return updated is null ? NotFound() : Redirect(ListUrl);
    }

    // What schools will see: the banner alone, in the public layout.
    [HttpGet("admin/home-banners/{id:int}/preview")]
    public async Task<IActionResult> Preview(int id)
    {
        var banner = await banners.GetByIdAsync(id);
        return banner is null ? NotFound() : View(banner);
    }

    [HttpGet("admin/home-banners/{id:int}/versions")]
    public async Task<IActionResult> Versions(int id)
    {
        var banner = await banners.GetByIdAsync(id);
        if (banner is null) return NotFound();
        return View(new HomeBannerVersionsViewModel { Banner = banner, Versions = await banners.GetVersionsAsync(id) });
    }

    [HttpPost("admin/home-banners/{id:int}/versions/{versionId:int}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, int versionId)
    {
        var restored = await banners.RestoreVersionAsync(id, versionId);
        return restored is null ? NotFound() : Redirect($"{ListUrl}/{id}/versions");
    }

    [HttpGet("admin/home-banners/{id:int}/delete")]
    public async Task<IActionResult> DeleteConfirm(int id)
    {
        var banner = await banners.GetByIdAsync(id);
        return banner is null
            ? NotFound()
            : View("Delete", new HomeBannerDeleteViewModel { Id = id, Heading = banner.Heading });
    }

    [HttpPost("admin/home-banners/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePost(int id) =>
        await banners.DeleteAsync(id) ? Redirect(ListUrl) : NotFound();

    [HttpPost("admin/home-banners/{id:int}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, string direction)
    {
        if (direction is not ("up" or "down")) return BadRequest();
        await banners.MoveAsync(id, direction);
        return Redirect(ListUrl);
    }
}
