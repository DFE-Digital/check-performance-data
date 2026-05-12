using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class TicketsViewModel
    {
        public ListViewTicketsResponseDto? TicketsResponse { get; set; }
        public TicketFieldsResponseDto? TicketFieldsResponse { get; set; }
    }
}
