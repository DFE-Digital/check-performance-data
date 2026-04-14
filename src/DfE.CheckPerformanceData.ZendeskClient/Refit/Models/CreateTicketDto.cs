//using Newtonsoft.Json;
//using System;
//using System.Collections.Generic;
//using System.Text;

//namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
//{
//    public class CreateTicketRequestDto
//    {
//        [JsonProperty("ticket")]
//        public CreateTicket Ticket { get; set; }
//    }

//    public class CreateTicket
//    {
//        [JsonProperty("subject")]
//        public string Subject { get; set; }

//        [JsonProperty("comment")]
//        public TicketComment Comment { get; set; }

//        [JsonProperty("requester_id")]
//        public long? RequesterId { get; set; }
//    }

//    public class TicketComment
//    {
//        [JsonProperty("body")]
//        public string Body { get; set; }
//    }
//}
