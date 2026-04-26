using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketsViewModel
    {
        public ListViewTicketsResponseDto TicketsResponse { get; set; } = new ListViewTicketsResponseDto();
        public TicketFieldsResponseDto TicketFieldsResponse { get; set; }  = new TicketFieldsResponseDto();
    }
}
