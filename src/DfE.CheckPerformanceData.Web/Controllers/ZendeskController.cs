using DfE.CheckPerformanceData.ZendeskClient.Refit;
using DfE.CheckPerformanceData.ZendeskClient.Refit.Models;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers
{
    public class ZendeskController : Controller
    {

        private readonly IZendeskApi zendeskApi;
        private View? ourView = null; // id 19337095327890
        public ZendeskController(IZendeskApi zendeskApi)
        {
            this.zendeskApi = zendeskApi;
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
            return View(results);
        }

        public async Task<IActionResult> UserFields()
        {
            var fields = await zendeskApi.GetUserFields();
            return View(fields);

        }

        public async Task<IActionResult> ViewTicket(long id)
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
            return View(model);
        }
    }
}
