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

        [JsonProperty("status")]
        public string Status { get; set; }
        [JsonProperty("group_id")]
        public long? GroupId { get; set; }


        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("comment")]
        public TicketComment Comment { get; set; }

        [JsonProperty("requester_id")]
        public long? RequesterId { get; set; }

        [JsonProperty("priority")]
        public string? Priority { get; set; }   // optional

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("custom_fields")]
        public List<CustomField> CustomFields { get; set; } = new List<CustomField>();
    }

    //public class TicketComment
    //{
    //    [JsonProperty("body")]
    //    public string Body { get; set; }
    //}
}
