
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services
{
    public interface IZendeskAttachmentService
    {
        Task<UpdateTicketResponseDto> AddAttachmentAsync(long ticketId, string fileName, Stream fileStream, string commentBody = "Attachment uploaded");
    }
}