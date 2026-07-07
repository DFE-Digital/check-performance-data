using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only trigger that copies the two hand-built guidance pages into the page tree as content-type
// nodes (guidance/landing-test and guidance/ks4-test), rebuilding them from their CMS content blocks.
// Gated exactly like the other /dev/* surfaces (Dev:ToolsEnabled AND a hard production guard) and
// [AllowAnonymous] to match them: it is reached before any auth cookie and only touches the local
// dev database.
[AllowAnonymous]
public sealed class DevGuidanceCopyController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly GuidanceContentCopyService _service;
    private readonly IHostEnvironment? _hostEnvironment;

    public DevGuidanceCopyController(
        IConfiguration configuration,
        GuidanceContentCopyService service,
        IHostEnvironment? hostEnvironment = null)
    {
        _configuration = configuration;
        _service = service;
        _hostEnvironment = hostEnvironment;
    }

    private bool IsAllowed =>
        _configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && _hostEnvironment?.IsProduction() != true;

    [HttpPost("dev/guidance-copy")]
    public async Task<IActionResult> Copy()
    {
        if (!IsAllowed)
            return NotFound();

        var result = await _service.CopyAsync(User?.Identity?.Name);

        if (result.GuidanceRootMissing)
            return Content("guidance node missing", "text/plain");

        return Json(new { ok = true, created = result.Created, updated = result.Updated });
    }
}
