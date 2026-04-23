using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Services
{
    public interface IZendeskAttachmentService
    {
        Task<UpdateTicketResponse> AddAttachmentAsync(long ticketId, string fileName, Stream fileStream, string commentBody = "Attachment uploaded");
    }
}