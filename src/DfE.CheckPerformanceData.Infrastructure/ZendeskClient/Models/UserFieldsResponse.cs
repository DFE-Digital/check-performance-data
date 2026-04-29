using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class UserFieldsResponse : BasePagedModelResponse
    {
        [JsonProperty("user_fields")]
        public List<CustomFieldMetaData> UserFields { get; set; }

        [JsonProperty("max_user_field_limit")]
        public int MaxUserFieldLimit { get; set; }
    }


}
