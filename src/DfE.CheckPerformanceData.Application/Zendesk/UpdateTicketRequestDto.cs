using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class UpdateTicketRequestDto
    {
        public UpdateTicketDto? Ticket { get; set; }
    }

    public class UpdateTicketDto
    {
        public TicketCommentUpdateDto? Comment { get; set; }
    }

    public class TicketCommentUpdateDto
    {
        public string? Body { get; set; }
        public List<string>? Uploads { get; set; }
    }
}
