using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class GetTicketViewModel
    {
        public TicketDto Ticket { get; set; }
        public List<CustomFieldMetaDataDto> UserFields { get; set; }
        public List<TicketCommentDto> Comments { get; set; }
    }
}
