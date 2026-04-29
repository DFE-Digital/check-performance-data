using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class GetTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket? Ticket { get; set; }
    }
}
