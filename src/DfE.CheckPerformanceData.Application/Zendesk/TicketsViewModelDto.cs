using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketsViewModel
    {
        public ListViewTicketsResponseDto? TicketsResponse { get; set; }
        public TicketFieldsResponseDto? TicketFieldsResponse { get; set; }
    }
}
