using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class CreateTicketRequestDto
    {
        [JsonPropertyName("ticket")]
        public CreateTicketDto Ticket { get; set; } = new();
    }

    public class CreateTicketDto
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("group_id")]
        public long? GroupId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("comment")]
        public TicketCommentDto? Comment { get; set; }

        [JsonPropertyName("requester_id")]
        public long? RequesterId { get; set; }

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("custom_fields")]
        public List<CustomFieldDto> CustomFields { get; set; } = new();

        [JsonPropertyName("brand_id")]
        public long? BrandId { get; set; }
    }
}
