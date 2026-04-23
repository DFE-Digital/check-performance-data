using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class ListViewsResponse : BasePagedModelResponse
    {
        [JsonProperty("views")]
        public List<View> Views { get; set; }

    }

}
