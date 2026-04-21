using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class CreateTicketRequest
    {
        /// <summary>
        /// The ticket details request payload.
        /// </summary>
        [JsonProperty("ticket")]
        public CreateTicket Ticket { get; set; }
    }

    public class CreateTicket
    {
        /// <summary>
        /// The subject or title of the ticket.
        /// </summary>
        [JsonProperty("subject")]
        public string Subject { get; set; }

        /// <summary>
        /// The current status of the ticket (e.g., Open, Closed).
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// The ID of the group the ticket belongs to.
        /// </summary>
        [JsonProperty("group_id")]
        public long? GroupId { get; set; }

        /// <summary>
        /// The type or category of the ticket.
        /// </summary>
        [JsonProperty("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Detailed comments or notes regarding the ticket.
        /// </summary>
        [JsonProperty("comment")]
        public TicketComment Comment { get; set; }

        /// <summary>
        /// The ID of the user who requested the ticket.
        /// </summary>
        [JsonProperty("requester_id")]
        public long? RequesterId { get; set; }

        /// <summary>
        /// The priority level of the ticket (optional).
        /// </summary>
        [JsonProperty("priority")]
        public string? Priority { get; set; }   // optional

        /// <summary>
        /// A detailed description of the ticket issue.
        /// </summary>
        [JsonProperty("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Custom fields or metadata associated with the ticket.
        /// </summary>
        [JsonProperty("custom_fields")]
        public List<CustomField> CustomFields { get; set; } = new List<CustomField>();

        /// <summary>
        /// The ID of the brand associated with the ticket.
        /// </summary>
        [JsonProperty("brand_id")]
        public long? BrandId { get; set; }
    }

}
