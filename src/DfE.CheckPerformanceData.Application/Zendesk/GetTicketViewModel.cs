using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class GetTicketViewModel
    {
        public TicketDto? Ticket { get; set; }
        public List<CustomFieldMetaDataDto> UserFields { get; set; } = new();
        public List<TicketCommentDto> Comments { get; set; } = new();
    }
}
