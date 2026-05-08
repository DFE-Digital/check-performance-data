using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class CreateTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket? Ticket { get; set; }
    }
}
