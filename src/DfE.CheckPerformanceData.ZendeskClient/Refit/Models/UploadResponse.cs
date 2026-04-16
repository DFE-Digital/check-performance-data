using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    //public class UploadResponse
    //{
    //    [JsonProperty("upload")]
    //    public Upload Upload { get; set; }
    //}

    //public class Upload
    //{
    //    [JsonProperty("token")]
    //    public string Token { get; set; }

    //    [JsonProperty("attachments")]
    //    public List<Attachment> Attachments { get; set; }
    //}

    //public class Attachment
    //{
    //    [JsonProperty("id")]
    //    public long Id { get; set; }

    //    [JsonProperty("file_name")]
    //    public string FileName { get; set; }

    //    [JsonProperty("content_url")]
    //    public string ContentUrl { get; set; }
    //}

    public class UploadResponse
    {
        [JsonProperty("upload")]
        public Upload Upload { get; set; }
    }

    public class Upload
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("attachments")]
        public List<Attachment> Attachments { get; set; }
    }

}