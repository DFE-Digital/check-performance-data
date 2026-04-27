using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class UpdateTicketResponseDto
    {
        public UpdatedTicketDto Ticket { get; init; } = default!;
        public TicketAuditDto Audit { get; init; } = default!;

    }
    
    public class UpdatedTicketDto
    {

        public long Id { get; init; }
        public string Status { get; init; } = default!;
        public DateTime UpdatedAt { get; init; }
        public TicketCommentDto? Comment { get; init; }
    }

    public class TicketAuditDto
    {
        public long Id { get; init; }
        public long TicketId { get; init; }
        public DateTime CreatedAt { get; init; }
        public long AuthorId { get; init; }
        public List<TicketAuditEventDto> Events { get; init; } = default!;
    }

    public class TicketAuditEventDto
    {
        public long Id { get; init; }
        public string Type { get; init; } = default!;
        public string Body { get; init; } = default!;
        public List<AttachmentDto>? Attachments  { get; init; } 
    }
}
