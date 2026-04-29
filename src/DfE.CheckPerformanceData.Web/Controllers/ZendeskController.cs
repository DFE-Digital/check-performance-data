using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using DfE.CheckPerformanceData.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DfE.CheckPerformanceData.Web.Controllers
{
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
                // Create a ticket based on pre-prod ticket 76218
                var request = new CreateTicketRequestDto
                {
                    Ticket = new CreateTicketDto
                    {
                        Subject = "School Checking Exercise",
                        Status = "new",
                        Type = "question",
                        GroupId = 16886472637330,
                        Description = @"REQUEST_ID: 85671\nSUBMISSION_ID: 85611\nrequest_StudentCYPMDID: 229520\nrequest_Student: KITTY VERA CALLAGHAN\nrequest_StudentOnRoll: null\nrequest_StudentRemoveCategory: Terminal/Critical illness\nrequest_StudentFirstName: KITTY VERA\nrequest_StudentSurname: CALLAGHAN\nrequest_StudentUPN: P228520169565\nrequest_MergeStudent: null\nrequest_MergeStudentFirstName: null\nrequest_MergeStudentSurname: null\nrequest_MergeStudentUPN: null\nrequest_StudentDfENoExcludingSchool: null\nrequest_StudentPermanentExclusion: null\nrequest_StudentArrivalDate: null\nrequest_StudentFirstLanguage: null\nrequest_StudentFirstSchoolAdmission: null\nrequest_StudentOriginCountry: null\nrequest_StudentSchoolAdmissionDate: null\nrequest_StudentNewYearGroup: null\nrequest_StudentPreviousKS4School: null\nrequest_StudentYearGroupChange: null\nrequest_StudentYearGroupChangeReason: null\nrequest_StudentDetained: null\nrequest_StudentPoliceInvolve: null\nrequest_StudentSatY11Exams: null\nrequest_StudentSocialCare: null\nrequest_StudentAbilityToAccessEducation: true\nrequest_StudentCriticalIllness: true\nrequest_StudentCriticalIllnessInvestigation: true\nrequest_StudentLifeChangingIllness: true\nrequest_StudentLifeChangingInjury: false\nrequest_StudentTerminalIllness: true\nrequest_StudentDateRemoved: null\nrequest_StudentOnRollCurrentYear: null\nrequest_StudentDfENoOnwardSchool: null\nrequest_StudentCurrentCountry: null\nrequest_StudentEffortToLocate: null\nrequest_StudentWhereabouts: null\nrequest_AdditionalComments: null\nrequest_ExceptionalCircumstances: false\nrequest_Outcome: Scrutiny\nrequest_RequiresEvidence: true\nrequest_StudentAddBack: false\nrequest_ZendeskID: null\nrequest_ZendeskURL: null\nrequest_StudentLDS: 30282884\nrequest_StudentEthnicity: WBRI\nrequest_StudentDfEEN: 8412647\nrequest_StudentSex: M\nrequest_StudentSENStatus: null\nrequest_StudentURN: 136989\nrequest_StudentInclusionStatusFlag: 201\nrequest_StudentAge: 0\nrequest_StudentAdmissionDate: 2016-01-04 00:00:00.0\nrequest_StudentDOB: 2012-12-21 00:00:00.0\nrequest_DecisionReason: null\nrequest_DecisionReasonRejected: null\nrequest_StudentRemoveCategoryUnderscore: Terminal_critical_illness\nrequest_CorrectionType: 31_\nrequest_CorrectionReason20: null\nrequest_CorrectionReason30: null\nrequest_CorrectionReason31: 503_31\nrequest_CycleYear: 2025\nrequest_CycleMonth: 9\nrequest_KeyStage: KS2\nrequest_MergeStudentLDS: null\nrequest_StudentContinuingKS2Studies: null\nrequest_StudentDateAdded: null\nrequest_StudentSatY6Exams: false\n",
                        CustomFields =
                        [
                            new CustomFieldDto { Id = 360013574700, Value = "training_provider" },
                            new CustomFieldDto { Id = 17207944800146, Value = "8412647" },
                            new CustomFieldDto { Id = 17207966711570, Value = "P228520169565" },
                            new CustomFieldDto { Id = 17207989310226, Value = "136989" },
                            new CustomFieldDto { Id = 17207993784978, Value = "30282884" },
                            new CustomFieldDto { Id = 17208002901906, Value = "2012-12-21" },
                            new CustomFieldDto { Id = 17208027233554, Value = "2016-01-04" },
                            new CustomFieldDto { Id = 19056253670034, Value = "scrutiny" },
                            new CustomFieldDto { Id = 19056595594898, Value = "31_" },
                            new CustomFieldDto { Id = 19058058434322, Value = "2025" },
                            new CustomFieldDto { Id = 19058091622546, Value = "9" },
                            new CustomFieldDto { Id = 19058126549778, Value = "ks2" },
                            new CustomFieldDto { Id = 19058409672594, Value = "CALLAGHAN" },
                            new CustomFieldDto { Id = 19058507283218, Value = "KITTY VERA" },
                            new CustomFieldDto { Id = 19058550118802, Value = "m" },
                            new CustomFieldDto { Id = 19058912556690, Value = "503_31" },
                            new CustomFieldDto { Id = 19381440546322, Value = "terminal_critical_illness" },
                            new CustomFieldDto { Id = 20433125966866, Value = "229520" }
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