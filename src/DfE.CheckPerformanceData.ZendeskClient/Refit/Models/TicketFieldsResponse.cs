using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class TicketFieldsResponse
    {
        [JsonProperty("ticket_fields")]
        public List<UserField> TicketFields { get; set; }
    }
}
