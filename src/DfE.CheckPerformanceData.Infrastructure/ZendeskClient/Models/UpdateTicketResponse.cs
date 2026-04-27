using DfE.CheckPerformanceData.Application.ZendeskClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using DfE.CheckPerformanceData.Infrastructure.Mappers;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class UpdateTicketResponse
    {
        [JsonProperty("ticket")]
        public UpdatedTicket Ticket { get; set; }  = default!;

        [JsonProperty("audit")]
        public TicketAudit Audit { get; set; }  = default!;


    }

    public class UpdatedTicket
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }  = default!;

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("comment")]
        public TicketComment? Comment { get; set; }
    }

    public class TicketAudit
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("ticket_id")]
        public long TicketId { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("author_id")]
        public long AuthorId { get; set; }

        [JsonProperty("events")]
        public List<TicketAuditEvent> Events { get; set; }  = default!;
    }

    public class TicketAuditEvent
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }  = default!;

        [JsonProperty("body")]
        public string Body { get; set; }  = default!;

        [JsonProperty("attachments")]
        public List<Attachment>? Attachments { get; set; }
    }


}
