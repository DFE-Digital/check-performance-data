
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class ListViewsResponse : BasePagedModelResponse
    {
        [JsonProperty("views")]
        public List<View> Views { get; set; }

    }

}
