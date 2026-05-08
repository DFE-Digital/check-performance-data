using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Refit;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    public interface IZendeskApi
    {
        [Post("/api/v2/tickets.json")]
        Task<CreateTicketResponse> CreateTicket([Body] CreateTicketRequest request);
       
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

        [Headers("Content-Type: application/binary")]
        [Post("/api/v2/uploads.json?filename={fileName}")]
        Task<UploadResponse> UploadFile(
            string fileName,
            [Body] Stream fileContent
        );

        [Put("/api/v2/tickets/{ticketId}.json")]
        Task<UpdateTicketResponse> AddCommentWithAttachment(
            long ticketId,
            [Body] UpdateTicketRequest request
        );



    }
}
