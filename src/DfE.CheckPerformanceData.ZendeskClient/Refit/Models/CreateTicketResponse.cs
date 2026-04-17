using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class CreateTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket Ticket { get; set; }
    }

}