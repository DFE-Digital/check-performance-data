using DfE.CheckPerformanceData.ZendeskClient.Models;
using DfE.CheckPerformanceData.ZendeskClient.Refit.Models;
using Refit;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit
{
    public interface IZendeskApi
    {
        [Post("/api/v2/tickets.json")]
        Task<CreateTicketResponse> CreateTicket([Body] CreateTicketRequest request);

        [Post("/api/v2/uploads.json")]
        Task<UploadResponse> UploadAttachment([Query] string filename, [Body] byte[] fileBytes);


        /// <summary>
        /// 
        /// </summary>
        /// <param name="query">Use the ListViewsRequest Model with the ToDictionary extension</param>
        /// <returns></returns>
        [Get("/api/v2/views.json")]
        Task<ListViewsResponse> GetViews([Query] Dictionary<string, object>? query = null);
        [Get("/api/v2/views.json")]
        Task<ListViewsResponse> GetViews([Query] int? per_page = null);

        [Get("/api/v2/views/{view_id}/tickets")]
        Task<ListViewTicketsResponse> GetTicketsForView(long view_id, [Query] Dictionary<string, object>? query = null);

        [Get("/api/v2/tickets/{ticket_id}")]
        Task<GetTicketResponse> GetTicket(long ticket_id);

        [Get("/api/v2/user_fields.json")]
 
        Task <UserFieldsResponse> GetUserFields();

        [Get("/api/v2/ticket_fields.json")]
        Task<TicketFieldsResponse> GetTicketFields();

        [Get("/api/v2/tickets/{ticket_id}/comments.json")]
        Task<TicketCommentsResponse> GetTicketComments(long ticket_id);

        // adding an attachment is a 2 step process. first upload a file to get an upload token:
        //POST /api/v2/uploads.json? filename = { fileName }
        //Content-Type: application/binary
        // then add a comment that refernces the upload token:
        //POST /api/v2/tickets/{ticket_id}.json

    /*
     * eg
    var fields = await _zendeskApi.GetTicketFields();
    var lookup = fields.TicketFields.ToDictionary(f => f.Id);

    foreach (var cf in ticket.CustomFields)
    {
        if (lookup.TryGetValue(cf.Id, out var fieldMeta))
        {
            Console.WriteLine($"{fieldMeta.Title}: {cf.Value}");
        }
    }
     * 
     */
}
}
/*
 * usage:
 * 
 * services.AddRefitClient<IZendeskApi>()
        .ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri("https://yourdomain.zendesk.com");
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("email/token:apitoken"));
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        });


 * var zendesk = RestService.For<IZendeskApi>("https://yourdomain.zendesk.com");
 * await zendesk.CreateTicket(new CreateTicketRequest { ... });
 * 
 * 
 * 
 * 
 * tactical methods and endpoints:
 * 
 * get_ticket_ids_from_view
 * url = f"{self.base_url}/views/{view_id}/tickets.json"
 * 
 * check_for_attachments
 *  url = f"{self.base_url}/tickets/{ticket_id}/comments.json"
 *  
 *  get_ticket_attachments
 *  url = f"{self.base_url}/tickets/{ticket_id}/comments.json"
 *  
 *  get_ticket_field_mapping:  Retrieves the ID, title, and selection options of custom fields.
 *  url = f"{self.base_url}/ticket_fields.json"
 *  
 *  get_tickets:  Retrieve tickets from Zendesk and map custom fields to readable names.
 *  url = f"{self.base_url}/tickets/show_many?ids={id_list}"
 *  
 *  get_ticket_comments
 *  url = f"{self.base_url}/tickets/{ticket_id}/comments.json"
 *  
 *  update_ticket
 *   url = f"{self.base_url}/tickets/{ticket_id}.json"
 *   
 *   
 *   tag_ticket
 *   url = f"{self.base_url}/tickets/{ticket_id}/tags.json"
 *   
 *   create_ticket
 *   url = f"{self.base_url}/tickets.json"  
 *   
 *   change_ticket_requester
 *   ticket_url = f'{self.base_url}/tickets/{ticket_id}.json'
 *   
 *   get_customer_details
 *    url = f"{self.base_url}/tickets/{ticket_id}.json"
 *    
 *    print_field_ids_for_form
 *    url = f"{self.base_url}/ticket_forms/{ticket_form_id}.json"
 *    
 *    
 */