using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class ListViewTicketsResponse : BasePagedModelResponse
    {
        [JsonProperty("tickets")]
        public List<Ticket> Tickets { get; set; }

    }

}
