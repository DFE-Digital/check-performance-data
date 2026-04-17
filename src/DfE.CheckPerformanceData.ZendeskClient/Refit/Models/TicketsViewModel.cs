using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class TicketsViewModel
    {
        public ListViewTicketsResponse TicketsResponse { get; set; } = new ListViewTicketsResponse();
        public TicketFieldsResponse TicketFieldsResponse { get; set; }  = new TicketFieldsResponse();
    }
}
