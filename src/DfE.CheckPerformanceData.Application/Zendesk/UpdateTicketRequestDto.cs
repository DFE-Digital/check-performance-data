using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class UpdateTicketRequestDto
    {
        public UpdateTicketDto? Ticket { get; set; }
    }

    public sealed class UpdateTicketDto
    {
        public TicketCommentUpdateDto? Comment { get; set; }
    }

    public sealed class TicketCommentUpdateDto
    {
        public string? Body { get; set; }
        public List<string>? Uploads { get; set; }
    }
}
