using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    /// <summary>
    /// Request payload for updating a Zendesk ticket.
    /// </summary>
    public class UpdateTicketRequest
    {
        [JsonProperty("ticket")]
        public UpdateTicket? Ticket { get; set; }
    }

    /// <summary>
    /// Ticket update data within an update request.
    /// </summary>
    public class UpdateTicket
    {
        [JsonProperty("comment")]
        public TicketCommentUpdate? Comment { get; set; }
    }

    /// <summary>
    /// Comment data for a ticket update.
    /// </summary>
    public class TicketCommentUpdate
    {
        [JsonProperty("body")]
        public string? Body { get; set; }

        [JsonProperty("uploads")]
        public List<string>? Uploads { get; set; }
    }
}
