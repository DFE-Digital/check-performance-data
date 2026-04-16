using DfE.CheckPerformanceData.ZendeskClient.Refit.Models;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Services
{
    public interface IZendeskAttachmentService
    {
        Task<UpdateTicketResponse> AddAttachmentAsync(long ticketId, string fileName, Stream fileStream, string commentBody = "Attachment uploaded");
    }
}