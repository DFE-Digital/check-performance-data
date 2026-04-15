using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class GetTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket Ticket { get; set; }
    }
}
