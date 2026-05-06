
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public interface IZendeskAttachmentService
    {
        Task<UpdateTicketResponseDto> AddAttachmentAsync(long ticketId, string fileName, Stream fileStream, string commentBody = "Attachment uploaded");
    }
}
