using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class UploadResponse
    {
        [JsonProperty("upload")]
        public Upload Upload { get; set; }  = default!;
    }

    public class Upload
    {
        [JsonProperty("token")]
        public string Token { get; set; } = default!;

        [JsonProperty("attachments")]
        public List<Attachment> Attachments { get; set; } = default!;
    }

}