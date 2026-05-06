using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using DfE.CheckPerformanceData.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DfE.CheckPerformanceData.Web.Controllers
{

    /// <summary>
    /// Restricts access to only work in the Development environment.
    /// Returns 403 Forbidden if accessed from any other environment.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class DevelopmentOnlyAuthorizeAttribute : TypeFilterAttribute
    {
        public DevelopmentOnlyAuthorizeAttribute()
            : base(typeof(DevelopmentOnlyAuthorizeFilter)) { }

        private class DevelopmentOnlyAuthorizeFilter : IAuthorizationFilter
        {
            private readonly IHostEnvironment _environment;

            public DevelopmentOnlyAuthorizeFilter(IHostEnvironment environment)
            {
                _environment = environment;
            }

            public void OnAuthorization(AuthorizationFilterContext context)
            {
                if (!_environment.IsDevelopment())
                {
                    context.Result = new ForbidResult(); // or new StatusCodeResult(403);
                    return;
                }
            }
        }
    }
    [Authorize]
    [DevelopmentOnlyAuthorize]
    public class ZendeskController : Controller
    {
        private readonly IZendeskService _zendeskService;
        private readonly IZendeskAttachmentService _zendeskAttachmentService;
        private readonly ILogger<ZendeskController> _logger;

        // View ID: 19337095327890
        private const string TargetViewTitle = "Schools checking exercise View";

        public ZendeskController(
            IZendeskService zendeskService,
            IZendeskAttachmentService zendeskAttachmentService,
            ILogger<ZendeskController> logger)
        {
            _zendeskService = zendeskService;
            _zendeskAttachmentService = zendeskAttachmentService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var views = await _zendeskService.ListViewsAsync(200);
                var targetView = views.Views?.SingleOrDefault(v => v.Title == TargetViewTitle);

                if (targetView == null)
                {
                    _logger.LogError("Could not find Zendesk view with title '{ViewTitle}'", TargetViewTitle);
                    TempData["Error"] = $"Unable to load Zendesk view: '{TargetViewTitle}' not found.";
                    return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
                }

                _logger.LogInformation("Found view: '{ViewTitle}' (ID: {ViewId})", targetView.Title, targetView.Id);

                return RedirectToAction(nameof(Tickets), new { viewId = targetView.Id, pageSize = 50, pageNumber = 1 });
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while loading views");
                TempData["Error"] = "Unable to load Zendesk views. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while loading Zendesk views");
                TempData["Error"] = "An unexpected error occurred. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        public async Task<IActionResult> Tickets(long viewId, int pageSize = 50, int pageNumber = 1)
        {
            if (viewId <= 0)
            {
                ModelState.AddModelError("viewId", "View ID must be a positive number.");
                return BadRequest(ModelState);
            }

            if (pageSize <= 0 || pageSize > 200)
            {
                ModelState.AddModelError("pageSize", "Page size must be between 1 and 200.");
                return BadRequest(ModelState);
            }

            if (pageNumber <= 0)
            {
                ModelState.AddModelError("pageNumber", "Page number must be a positive number.");
                return BadRequest(ModelState);
            }

            try
            {
                var model = await _zendeskService.GetTicketsViewModelAsync(
                    viewId,
                    new ListViewTicketsRequest { PerPage = pageSize, Page = pageNumber }.ToQueryDictionary());

                return View(model);
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while loading tickets for view {ViewId}", viewId);
                TempData["Error"] = "Unable to load tickets. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while loading tickets for view {ViewId}", viewId);
                TempData["Error"] = "An unexpected error occurred. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        public async Task<IActionResult> UserFields()
        {
            try
            {
                var fields = await _zendeskService.GetUserFieldsAsync();
                return View(fields);
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while loading user fields");
                TempData["Error"] = "Unable to load user fields. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while loading user fields");
                TempData["Error"] = "An unexpected error occurred. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        public async Task<IActionResult> TicketFields()
        {
            try
            {
                var fields = await _zendeskService.GetTicketFields();
                return View(fields);
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while loading ticket fields");
                TempData["Error"] = "Unable to load ticket fields. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while loading ticket fields");
                TempData["Error"] = "An unexpected error occurred. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        public async Task<IActionResult> ViewTicket(long id, bool showNullValues = false)
        {
            if (id <= 0)
            {
                ModelState.AddModelError("id", "Ticket ID must be a positive number.");
                return BadRequest(ModelState);
            }

            try
            {
                var model = await _zendeskService.GetTicketViewModelAsync(id);
                ViewBag.ShowNullValues = showNullValues;
                return View(model);
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while loading ticket {TicketId}", id);
                TempData["Error"] = "Unable to load ticket. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while loading ticket {TicketId}", id);
                TempData["Error"] = "An unexpected error occurred. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddAttachment(long ticketId, IFormFile? fileUpload)
        {
            const long maxFileSizeBytes = 10 * 1024 * 1024; // Zendesk limit

            if (ticketId <= 0)
            {
                ModelState.AddModelError("ticketId", "Ticket ID must be a positive number.");
                return BadRequest(ModelState);
            }

            if (fileUpload == null || fileUpload.Length == 0)
            {
                ModelState.AddModelError("fileUpload", "Please select a file to upload.");
                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }

            if (string.IsNullOrWhiteSpace(fileUpload.FileName))
            {
                ModelState.AddModelError("fileUpload", "The filename is required.");
                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }

            if (fileUpload.Length > maxFileSizeBytes)
            {
                ModelState.AddModelError("fileUpload", $"File size must not exceed 10MB. Your file is {(fileUpload.Length / 1024F).ToString("F1")}KB.");
                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }

            try
            {
                using var stream = fileUpload.OpenReadStream();
                var result = await _zendeskAttachmentService.AddAttachmentAsync(
                    ticketId: ticketId,
                    fileName: fileUpload.FileName,
                    fileStream: stream,
                    commentBody: "Evidence file attached by Refit"
                );

                var attachment = result.Audit?.Events?
                    .FirstOrDefault(e => e.Attachments?.Any() == true)?
                    .Attachments?
                    .LastOrDefault();

                _logger.LogInformation("Successfully uploaded attachment '{FileName}' to ticket {TicketId}",
                    attachment?.FileName, ticketId);

                TempData["Success"] = $"File '{fileUpload.FileName}' uploaded successfully.";

                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while uploading attachment to ticket {TicketId}", ticketId);
                TempData["Error"] = "Unable to upload file. Please try again later.";
                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while uploading attachment to ticket {TicketId}", ticketId);
                TempData["Error"] = "An unexpected error occurred while uploading the file.";
                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }
        }

        public async Task<IActionResult> CreateTicket()
        {
            try
            {
                // Demo: create a ticket based on pre-prod ticket 76218.
                // Replace with real data or remove before production deployment.

                var request = new CreateTicketRequestDto
                {
                    Ticket = new CreateTicketDto
                    {
                        Subject = "School Checking Exercise",
                        Status = "new",
                        Type = "question",
                        GroupId = 16886472637330,
                        Description = @"REQUEST_ID: [PLACEHOLDER]\nSUBMISSION_ID: [PLACEHOLDER]\nrequest_StudentFirstName: [PLACEHOLDER]\nrequest_StudentSurname: [PLACEHOLDER]\nrequest_StudentUPN: [PLACEHOLDER]",
                        CustomFields =
                        [
                            new CustomFieldDto { Id = 360013574700, Value = "training_provider" },
                            new CustomFieldDto { Id = 17207944800146, Value = "8412647" }, // DFE ESTABLISHMENT NUMBER
                            new CustomFieldDto { Id = 17207966711570, Value = "P228520163345" }, // UPN
                            new CustomFieldDto { Id = 17207989310226, Value = "136989" }, // SCHOOL URN
                            new CustomFieldDto { Id = 17207993784978, Value = "30280000" },// LDS matched pupil ID
                            new CustomFieldDto { Id = 17208002901906, Value = "2012-12-21" },
                            new CustomFieldDto { Id = 17208027233554, Value = "2016-01-04" },
                            new CustomFieldDto { Id = 19056253670034, Value = "scrutiny" },
                            new CustomFieldDto { Id = 19056595594898, Value = "31_" },
                            new CustomFieldDto { Id = 19058058434322, Value = "2025" },
                            new CustomFieldDto { Id = 19058091622546, Value = "9" },
                            new CustomFieldDto { Id = 19058126549778, Value = "ks2" },
                            new CustomFieldDto { Id = 19058409672594, Value = "CRIPPS" },
                            new CustomFieldDto { Id = 19058507283218, Value = "PAUL" },
                            new CustomFieldDto { Id = 19058550118802, Value = "m" },
                            new CustomFieldDto { Id = 19058912556690, Value = "503_31" },
                            new CustomFieldDto { Id = 19381440546322, Value = "terminal_critical_illness" },
                            new CustomFieldDto { Id = 20433125966866, Value = "229000" } // CYPMD_ID
                        ],
                        BrandId = 16853215883538
                    }
                };

                var response = await _zendeskService.CreateTicketAsync(request);

                _logger.LogInformation("Successfully created ticket {TicketId}", response.Ticket?.Id);

                return RedirectToAction(nameof(ViewTicket), new { id = response.Ticket?.Id });
            }
            catch (ZendeskApiException ex)
            {
                _logger.LogError(ex, "Zendesk API error while creating ticket");
                TempData["Error"] = "Unable to create ticket. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating ticket");
                TempData["Error"] = "An unexpected error occurred. Please try again later.";
                return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }
    }
}