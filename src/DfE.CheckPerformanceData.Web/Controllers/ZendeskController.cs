using DfE.CheckPerformanceData.ZendeskClient.Refit;
using DfE.CheckPerformanceData.ZendeskClient.Refit.Models;
using DfE.CheckPerformanceData.ZendeskClient.Refit.Services;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers
{
    public class ZendeskController : Controller
    {

        private readonly IZendeskApi zendeskApi;
        //private readonly IZendeskAttachmentService zendeskAttachmentService;
        private View? ourView = null; // id 19337095327890
        public ZendeskController(IZendeskApi zendeskApi)//, IZendeskAttachmentService zendeskAttachmentService)
        {
            this.zendeskApi = zendeskApi;
            //this.zendeskAttachmentService = zendeskAttachmentService;
        }
        public async Task<IActionResult> Index()
        {

            var views = await zendeskApi.GetViews(200);
            ourView = views.Views.SingleOrDefault(v => v.Title == "Schools checking exercise View"); //CYPMD View");
            System.Diagnostics.Debug.WriteLine($"Found view: {ourView?.Title} (ID: {ourView?.Id})");
            //zendeskApi.GetViews(200);
            //    //new ListViewsRequest{ PerPage = 200 }.ToQueryDictionary())
            //                     .ContinueWith(task =>
            //{
            //    if (task.IsCompletedSuccessfully)
            //    {
            //        var views = task.Result;
            //        // Do something with the views
            //        ourView = views.Views.SingleOrDefault(v => v.Title == "Schools checking exercise View"); //CYPMD View");
            //        System.Diagnostics.Debug.WriteLine($"Found view: {ourView?.Title} (ID: {ourView?.Id})");
            //    }
            //    else
            //    {
            //        // Handle error
            //    }
            //});

            return RedirectToAction(nameof(Tickets), new { viewId = ourView.Id, pageSize = 50, PageNumber = 1 });
            return View(ourView);
        }

        public async Task<IActionResult> Tickets(long viewId, int pageSize, int PageNumber)
        {
            var results = await zendeskApi.GetTicketsForView(viewId, new ListViewTicketsRequest { PerPage = pageSize, Page = PageNumber }.ToQueryDictionary());
            var meta = await zendeskApi.GetTicketFields();
            var model = new TicketsViewModel
            {
                TicketsResponse = results,
                TicketFieldsResponse = meta
            };

            return View(model);
        }

        public async Task<IActionResult> UserFields()
        {
            var fields = await zendeskApi.GetUserFields();
            return View(fields);

        }

        public async Task<IActionResult> ViewTicket(long id, bool showNullValues = false)
        {
            var ticket = await zendeskApi.GetTicket(id);
            var model = new GetTicketViewModel
            {
                Ticket = ticket.Ticket,
                UserFields = (await zendeskApi.GetTicketFields()).TicketFields,
                Comments = (await zendeskApi.GetTicketComments(id)).Comments
                //.Where(f => ticket.Ticket.CustomFields.Any(cf => cf.Id == f.Id))
                //.Select(f => new UserField
                //{
                //    Id = f.Id,
                //    Key = f.Key,
                //    Title = f.Title,
                //    Type = f.Type
                //})
            };
            ViewBag.ShowNullValues = showNullValues;
            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> AddAttachment(long ticketId, IFormFile fileUpload)
        {
            using (var stream = fileUpload.OpenReadStream())
            {
                var result = await new ZendeskAttachmentService(zendeskApi).AddAttachmentAsync(
                    ticketId: ticketId,
                    fileName: fileUpload.FileName,
                    fileStream: stream,
                    commentBody: "Evidence file attached by Refit"
                );
                // Access the attachment Zendesk created
                //var attachment = result.Ticket.Comment.Attachments.FirstOrDefault();
                var attachment = result.Audit.Events
                    .FirstOrDefault(e => e.Attachments?.Any() == true)?
                    .Attachments
                    .LastOrDefault();

                System.Diagnostics.Debug.WriteLine($"Uploaded: {attachment?.FileName}");

                
                return RedirectToAction(nameof(ViewTicket), new { id = ticketId });
            }

        }

        public async Task<IActionResult> CreateTicket()
        {
            // for now create a ticket based on pre prod 76218
            // set the description to be the same and use the same custom fields that are populated. dont add tags
            var request = new CreateTicketRequest
            {
                Ticket = new CreateTicket
                {
                    Subject = "School Checking Exercise",
                    Status = "new",
                    Type = "question",
                    GroupId = 16886472637330,
                    Description = @"REQUEST_ID: 85671\nSUBMISSION_ID: 85611\nrequest_StudentCYPMDID: 229520\nrequest_Student: KITTY VERA CALLAGHAN\nrequest_StudentOnRoll: null\nrequest_StudentRemoveCategory: Terminal/Critical illness\nrequest_StudentFirstName: KITTY VERA\nrequest_StudentSurname: CALLAGHAN\nrequest_StudentUPN: P228520169565\nrequest_MergeStudent: null\nrequest_MergeStudentFirstName: null\nrequest_MergeStudentSurname: null\nrequest_MergeStudentUPN: null\nrequest_StudentDfENoExcludingSchool: null\nrequest_StudentPermanentExclusion: null\nrequest_StudentArrivalDate: null\nrequest_StudentFirstLanguage: null\nrequest_StudentFirstSchoolAdmission: null\nrequest_StudentOriginCountry: null\nrequest_StudentSchoolAdmissionDate: null\nrequest_StudentNewYearGroup: null\nrequest_StudentPreviousKS4School: null\nrequest_StudentYearGroupChange: null\nrequest_StudentYearGroupChangeReason: null\nrequest_StudentDetained: null\nrequest_StudentPoliceInvolve: null\nrequest_StudentSatY11Exams: null\nrequest_StudentSocialCare: null\nrequest_StudentAbilityToAccessEducation: true\nrequest_StudentCriticalIllness: true\nrequest_StudentCriticalIllnessInvestigation: true\nrequest_StudentLifeChangingIllness: true\nrequest_StudentLifeChangingInjury: false\nrequest_StudentTerminalIllness: true\nrequest_StudentDateRemoved: null\nrequest_StudentOnRollCurrentYear: null\nrequest_StudentDfENoOnwardSchool: null\nrequest_StudentCurrentCountry: null\nrequest_StudentEffortToLocate: null\nrequest_StudentWhereabouts: null\nrequest_AdditionalComments: null\nrequest_ExceptionalCircumstances: false\nrequest_Outcome: Scrutiny\nrequest_RequiresEvidence: true\nrequest_StudentAddBack: false\nrequest_ZendeskID: null\nrequest_ZendeskURL: null\nrequest_StudentLDS: 30282884\nrequest_StudentEthnicity: WBRI\nrequest_StudentDfEEN: 8412647\nrequest_StudentSex: M\nrequest_StudentSENStatus: null\nrequest_StudentURN: 136989\nrequest_StudentInclusionStatusFlag: 201\nrequest_StudentAge: 0\nrequest_StudentAdmissionDate: 2016-01-04 00:00:00.0\nrequest_StudentDOB: 2012-12-21 00:00:00.0\nrequest_DecisionReason: null\nrequest_DecisionReasonRejected: null\nrequest_StudentRemoveCategoryUnderscore: Terminal_critical_illness\nrequest_CorrectionType: 31_\nrequest_CorrectionReason20: null\nrequest_CorrectionReason30: null\nrequest_CorrectionReason31: 503_31\nrequest_CycleYear: 2025\nrequest_CycleMonth: 9\nrequest_KeyStage: KS2\nrequest_MergeStudentLDS: null\nrequest_StudentContinuingKS2Studies: null\nrequest_StudentDateAdded: null\nrequest_StudentSatY6Exams: false\n",
                    //RequesterId = 26701781440530, // tony bird
                    
                    CustomFields =
                    [
                        //new CustomField
                        //{
                        //    Id = 360013574700,
                        //    Value = "training_provider"
                        //},
                        //new CustomField
                        //{
                        //    Id = ,
                        //    Value = ""
                        //},
                        //new CustomField
                        //{
                        //    Id = ,
                        //    Value = ""
                        //},
                        //new CustomField
                        //{
                        //    Id = ,
                        //    Value = ""
                        //},
                        //new CustomField
                        //{
                        //    Id = ,
                        //    Value = ""
                        //},

                        new CustomField
                        {
                            Id = 360013574700,
                            Value = "training_provider"
                        },
                        new CustomField
                        {
                            Id = 17207944800146,
                            Value = "8412647"
                        },
                        new CustomField
                        {
                            Id = 17207966711570,
                            Value = "P228520169565"
                        },
                        new CustomField
                        {
                            Id = 17207989310226,
                            Value = "136989"
                        },
                        new CustomField
                        {
                            Id = 17207993784978,
                            Value = "30282884"
                        },
                        new CustomField
                        {
                            Id = 17208002901906,
                            Value = "2012-12-21"
                        },
                        new CustomField
                        {
                            Id = 17208027233554,
                            Value = "2016-01-04"
                        },
                        new CustomField
                        {
                            Id = 19056253670034,
                            Value = "scrutiny" //19103017562770//"scrutiny"
                        },
                        new CustomField
                        {
                            Id = 19056595594898,
                            Value = "31_"
                        },
                        new CustomField
                        {
                            Id = 19058058434322,
                            Value = "2025"
                        },
                        new CustomField
                        {
                            Id = 19058091622546,
                            Value = "9"
                        },
                        new CustomField
                        {
                            Id = 19058126549778,
                            Value = "ks2"
                        },
                        new CustomField
                        {
                            Id = 19058409672594,
                            Value = "CALLAGHAN"
                        },
                        new CustomField
                        {
                            Id = 19058507283218,
                            Value = "KITTY VERA"
                        },
                        new CustomField
                        {
                            Id = 19058550118802,
                            Value = "m"
                        },
                        new CustomField
                        {
                            Id = 19058912556690,
                            Value = "503_31"
                        },
                        new CustomField
                        {
                            Id = 19381440546322,
                            Value = "terminal_critical_illness"
                        },
                        new CustomField
                        {
                            Id = 20433125966866,
                            Value = "229520"
                        }

                    ],
                    BrandId = 16853215883538
                }
            };
            var response =await zendeskApi.CreateTicket(request);
            return RedirectToAction(nameof(ViewTicket), new { id = response.Ticket.Id });
        }
    }
}
