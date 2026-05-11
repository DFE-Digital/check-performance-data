using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public sealed class CreateTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket? Ticket { get; set; }
    }
}
