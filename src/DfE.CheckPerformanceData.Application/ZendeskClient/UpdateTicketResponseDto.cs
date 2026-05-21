using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class UpdateTicketResponseDto
    {
        public UpdatedTicketDto Ticket { get; init; } = new();

        public TicketAuditDto Audit { get; init; } = new();
    }

    public sealed class UpdatedTicketDto
    {
        public long Id { get; init; }

        public string Status { get; init; } = string.Empty;

        public DateTime UpdatedAt { get; init; }

        public TicketCommentDto? Comment { get; init; }
    }

    public sealed class TicketAuditDto
    {
        public long Id { get; init; }

        public long TicketId { get; init; }

        public DateTime CreatedAt { get; init; }

        public long AuthorId { get; init; }

        public List<TicketAuditEventDto> Events { get; init; } = new();
    }

    public sealed class TicketAuditEventDto
    {
        public long Id { get; init; }

        public string Type { get; init; } = string.Empty;

        public string Body { get; init; } = string.Empty;

        public List<AttachmentDto>? Attachments { get; init; }
    }
}
