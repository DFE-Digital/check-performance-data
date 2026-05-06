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

        //[Post("/api/v2/uploads.json")]
        //Task<UploadResponse> UploadAttachment([Query] string filename, [Body] byte[] fileBytes);


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


        // Step 1: Upload file
        //[Post("/api/v2/uploads.json")]
        //Task<UploadResponse> UploadFile(
        //    [AliasAs("filename")] string fileName,
        //    [Body] Stream fileContent
        //);

        [Headers("Content-Type: application/binary")]
        [Post("/api/v2/uploads.json?filename={fileName}")]
        Task<UploadResponse> UploadFile(
            string fileName,
            [Body] Stream fileContent
        );


        // Step 2: Add comment with attachment
        [Put("/api/v2/tickets/{ticketId}.json")]
        Task<UpdateTicketResponse> AddCommentWithAttachment(
            long ticketId,
            [Body] UpdateTicketRequest request
        );



    }
}
