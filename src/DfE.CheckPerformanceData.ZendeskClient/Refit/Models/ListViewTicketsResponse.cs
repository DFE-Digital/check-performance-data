using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class ListViewTicketsResponse : BasePagedModelResponse
    {
        [JsonProperty("tickets")]
        public List<Ticket> Tickets { get; set; }

        //[JsonProperty("count")]
        //public int Count { get; set; }

        //[JsonProperty("next_page")]
        //public string NextPage { get; set; }

        //[JsonProperty("previous_page")]
        //public string PreviousPage { get; set; }
    }

}
