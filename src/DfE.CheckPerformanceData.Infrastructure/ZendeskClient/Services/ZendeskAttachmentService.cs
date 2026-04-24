//using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Application;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services
{
    public class ZendeskAttachmentService : IZendeskAttachmentService
    {
        private readonly IZendeskApi _api;

        public ZendeskAttachmentService(IZendeskApi api)
        {
            _api = api;
        }

        /// <summary>
        /// Uploads a file to Zendesk and attaches it to an existing ticket as a comment.
        /// </summary>
        public async Task<UpdateTicketResponse> AddAttachmentAsync(
            long ticketId,
            string fileName,
            Stream fileStream,
            string commentBody = "Attachment uploaded"
        )
        {
            if (ticketId == null || ticketId == 0)
                throw new ArgumentException("Ticket Id is not valid.", nameof(ticketId));
            if (fileStream == null || fileStream.Length == 0)
                throw new ArgumentException("File stream is empty", nameof(fileStream));

            // STEP 1 — Upload file to Zendesk
            var uploadResponse = await _api.UploadFile(fileName, fileStream);

            var token = uploadResponse?.Upload?.Token;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Zendesk did not return an upload token.");

            // STEP 2 — Add comment referencing the upload token
            var request = new UpdateTicketRequest
            {
                Ticket = new UpdateTicket
                {
                    Comment = new TicketCommentUpdate
                    {
                        Body = commentBody,
                        Uploads = new List<string> { token }
                    }
                }
            };

            return await _api.AddCommentWithAttachment(ticketId, request);
        }
    }

}
/*
 * usage:
 * using var stream = System.IO.File.OpenRead("Accept Evidence.pdf");

var result = await _attachmentService.AddAttachmentAsync(
    ticketId: 86231,
    fileName: "Accept Evidence.pdf",
    fileStream: stream,
    commentBody: "Evidence file attached"
);

// Access the attachment Zendesk created
var attachment = result.Ticket.Comment.Attachments.FirstOrDefault();
Console.WriteLine($"Uploaded: {attachment?.FileName}");

 * 
 * 
 * 
 */