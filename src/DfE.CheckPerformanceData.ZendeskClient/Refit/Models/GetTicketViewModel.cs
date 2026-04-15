using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class GetTicketViewModel
    {
        public Ticket Ticket { get; set; }
        public List<UserField> UserFields { get; set; }
        public List<TicketComment> Comments { get; set; }
    }
}
