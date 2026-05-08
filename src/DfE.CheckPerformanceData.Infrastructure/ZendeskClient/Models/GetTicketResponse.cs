using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public sealed class GetTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket? Ticket { get; set; }
    }
}
