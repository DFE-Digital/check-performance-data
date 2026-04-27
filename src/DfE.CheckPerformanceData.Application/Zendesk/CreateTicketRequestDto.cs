using System.Collections.Generic;
using System.Text.Json.Serialization; // Replace Newtonsoft with System.Text.Json for serialization
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class CreateTicketRequestDto
    {
        [JsonPropertyName("ticket")]
        public CreateTicketDto Ticket  { get; set; }
    }

    public class CreateTicketDto
    {
        [JsonPropertyName("subject")]
        public string Subject  { get; set; } =default!;

        [JsonPropertyName("status")]
        public string Status  { get; set; } =default!;

        [JsonPropertyName("group_id")]
        public long? GroupId  { get; set; }

        [JsonPropertyName("type")]
        public string Type  { get; set; } =default!;

        [JsonPropertyName("comment")]
        public TicketCommentDto? Comment  { get; set; } 

        [JsonPropertyName("requester_id")]
        public long? RequesterId  { get; set; }

        [JsonPropertyName("priority")]
        public string Priority  { get; set; } =default!;    // optional

        [JsonPropertyName("description")]
        public string Description  { get; set; } =default!;

        [JsonPropertyName("custom_fields")]
        public List<CustomFieldDto> CustomFields  { get; set; } = new List<CustomFieldDto>();

        [JsonPropertyName("brand_id")]
        public long? BrandId  { get; set; }
    }
}
