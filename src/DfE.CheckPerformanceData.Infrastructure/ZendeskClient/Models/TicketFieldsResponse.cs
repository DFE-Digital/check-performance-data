using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    //public class TicketFieldsResponse
    //{
    //    public List<CustomFieldMetaData> TicketFields { get; init; } = new();
    //}

    public class TicketFieldsResponse :  BasePagedModelResponse
    {
        [JsonProperty("ticket_fields")]
        public List<CustomFieldMetaData> TicketFields { get; set; }

        [JsonProperty("max_user_field_limit")]
        public int MaxUserFieldLimit { get; set; }
    }
}
