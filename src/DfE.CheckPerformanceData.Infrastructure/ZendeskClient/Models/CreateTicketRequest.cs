using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class CreateTicketRequest
    {
        [JsonProperty("ticket")]
        public CreateTicketBody? Ticket { get; set; }
    }

    public class CreateTicketBody
    {
        [JsonProperty("subject")]
        public string? Subject { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonProperty("group_id")]
        public long? GroupId { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("comment")]
        public TicketComment? Comment { get; set; }

        [JsonProperty("requester_id")]
        public long? RequesterId { get; set; }

        [JsonProperty("priority")]
        public string? Priority { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("custom_fields")]
        public List<CustomField>? CustomFields { get; set; }

        [JsonProperty("brand_id")]
        public long? BrandId { get; set; }
    }
}
