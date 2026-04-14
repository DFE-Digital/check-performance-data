using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class BasePagedModelResponse
    {

        [JsonProperty("next_page")]
        public Uri? NextPage { get; set; }

        [JsonProperty("previous_page")]
        public Uri? PreviousPage { get; set; }
        [JsonProperty("count")]
        public int Count { get; set; }

        // todo add helpers and extensions to get query string from next/previous
    }
}
