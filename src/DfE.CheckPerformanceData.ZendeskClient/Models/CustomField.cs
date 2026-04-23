using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class CustomField
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("value")]
        public object? Value { get; set; }
    }
}
