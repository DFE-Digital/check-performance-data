using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    /// <summary>
    /// Request payload for updating a Zendesk ticket.
    /// </summary>
    public sealed class UpdateTicketRequest
    {
        [JsonProperty("ticket")]
        public UpdateTicket? Ticket { get; set; }
    }

    /// <summary>
    /// Ticket update data within an update request.
    /// </summary>
    public sealed class UpdateTicket
    {
        [JsonProperty("comment")]
        public TicketCommentUpdate? Comment { get; set; }
    }

    /// <summary>
    /// Comment data for a ticket update.
    /// </summary>
    public sealed class TicketCommentUpdate
    {
        [JsonProperty("body")]
        public string? Body { get; set; }

        [JsonProperty("uploads")]
        public List<string>? Uploads { get; set; }
    }
}
