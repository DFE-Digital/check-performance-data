using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class CreateTicketRequestDto
    {
        [JsonPropertyName("ticket")]
        public CreateTicketDto Ticket { get; set; } = new();
    }

    public sealed class CreateTicketDto
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("group_id")]
        public long? GroupId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>The first comment. The ticket builders send it as an internal note, so
        /// Zendesk does not add the agent signature to it.</summary>
        [JsonPropertyName("comment")]
        public CreateTicketCommentDto? Comment { get; set; }

        [JsonPropertyName("requester_id")]
        public long? RequesterId { get; set; }

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = string.Empty;

        [JsonPropertyName("custom_fields")]
        public List<CustomFieldDto> CustomFields { get; set; } = new();

        [JsonPropertyName("brand_id")]
        public long? BrandId { get; set; }

        /// <summary>The ticket form. Only a results enquiry ticket sets it. Null is left out of
        /// the request, so Zendesk uses its default form.</summary>
        [JsonPropertyName("ticket_form_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? TicketFormId { get; set; }
    }

    /// <summary>
    /// The first comment of a new ticket. A public comment from an agent gets the agent
    /// signature, which looks like an email to the school. So the ticket builders send
    /// <c>Public = false</c> (an internal note).
    /// </summary>
    public sealed class CreateTicketCommentDto
    {
        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("public")]
        public bool Public { get; set; }

        /// <summary>An internal note with the given text.</summary>
        public static CreateTicketCommentDto InternalNote(string body) => new() { Body = body, Public = false };
    }
}
