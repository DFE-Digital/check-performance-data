using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class UpdateTicketResponseDto
    {
        public UpdatedTicketDto Ticket;
        public TicketAuditDto Audit;
    }

    public class UpdatedTicketDto
    {
        public long Id;
        public string Status;
        public DateTime UpdatedAt;
        public TicketCommentDto Comment;
    }

    public class TicketAuditDto
    {
        public long Id;
        public long TicketId;
        public DateTime CreatedAt;
        public long AuthorId;
        public List<TicketAuditEventDto> Events;
    }

    public class TicketAuditEventDto
    {
        public long Id;
        public string Type;
        public string Body;
        public List<AttachmentDto> Attachments;
    }
}
