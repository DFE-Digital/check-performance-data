using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class UpdateTicketRequest
    {
        [JsonProperty("ticket")]
        public UpdateTicket Ticket { get; set; }
    }

    public class UpdateTicket
    {
        [JsonProperty("comment")]
        public TicketCommentUpdate Comment { get; set; }
    }

    public class TicketCommentUpdate
    {
        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("uploads")]
        public List<string> Uploads { get; set; }
    }

}
