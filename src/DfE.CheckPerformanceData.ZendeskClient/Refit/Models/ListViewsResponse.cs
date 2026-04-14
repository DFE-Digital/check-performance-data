
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class ListViewsResponse
    {
        [JsonProperty("views")]
        public List<View> Views { get; set; }

        [JsonProperty("next_page")]
        public Uri? NextPage { get; set; }

        [JsonProperty("previous_page")]
        public Uri? PreviousPage { get; set; }
        [JsonProperty("count")]
        public int Count { get; set; }
    }

}
