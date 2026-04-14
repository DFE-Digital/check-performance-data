//using System;
//using System.Collections.Generic;
//using System.Text;

//namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
//{
//    public class CreateTicketRequest
//    {
//        public TicketWrapper Ticket { get; set; }
//    }

//    public class TicketWrapper
//    {
//        public string Subject { get; set; }
//        public string Comment { get; set; }
//        public long? RequesterId { get; set; }
//    }
//}


using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class CreateTicketRequest
    {
        [JsonProperty("ticket")]
        public CreateTicket Ticket { get; set; }
    }

    public class CreateTicket
    {
        [JsonProperty("subject")]
        public string Subject { get; set; }

        [JsonProperty("comment")]
        public TicketComment Comment { get; set; }

        [JsonProperty("requester_id")]
        public long? RequesterId { get; set; }

        [JsonProperty("priority")]
        public string Priority { get; set; }   // optional
    }

    public class TicketComment
    {
        [JsonProperty("body")]
        public string Body { get; set; }
    }
}
