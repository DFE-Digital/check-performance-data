using Newtonsoft.Json;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class TicketComment
    {
        [JsonProperty("id")]
        public long? Id { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("author_id")]
        public long? AuthorId { get; set; }

        [JsonProperty("body")]
        public string? Body { get; set; }

        [JsonProperty("html_body")]
        public string? HtmlBody { get; set; }

        [JsonProperty("plain_body")]
        public string? PlainBody { get; set; }

        [JsonProperty("public")]
        public bool? Public { get; set; }

        [JsonProperty("attachments")]
        public List<Attachment>? Attachments { get; set; }

        [JsonProperty("audit_id")]
        public long? AuditId { get; set; }

        [JsonProperty("via")]
        public Via? Via { get; set; }

        [JsonProperty("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonProperty("metadata")]
        public CommentMetadata? Metadata { get; set; }
    }

    public class TicketCommentsResponse
    {
        [JsonProperty("comments")]
        public List<TicketComment>? Comments { get; set; }

        [JsonProperty("next_page")]
        public string? NextPage { get; set; }

        [JsonProperty("previous_page")]
        public string? PreviousPage { get; set; }

        [JsonProperty("count")]
        public int? Count { get; set; }
    }

    public class Via
    {
        [JsonProperty("channel")]
        public string? Channel { get; set; }

        [JsonProperty("source")]
        public ViaSource? Source { get; set; }
    }

    public class ViaSource
    {
        [JsonProperty("from")]
        public Dictionary<string, object>? From { get; set; }

        [JsonProperty("to")]
        public Dictionary<string, object>? To { get; set; }

        [JsonProperty("rel")]
        public string? Rel { get; set; }
    }

    public class CommentMetadata
    {
        [JsonProperty("system")]
        public MetadataSystem? System { get; set; }

        [JsonProperty("custom")]
        public Dictionary<string, object>? Custom { get; set; }
    }

    public class MetadataSystem
    {
        [JsonProperty("client")]
        public string? Client { get; set; }

        [JsonProperty("ip_address")]
        public string? IpAddress { get; set; }

        [JsonProperty("location")]
        public string? Location { get; set; }

        [JsonProperty("latitude")]
        public double? Latitude { get; set; }

        [JsonProperty("longitude")]
        public double? Longitude { get; set; }
    }

    public class Attachment
    {
        [JsonProperty("content-type")]
        public string? ContentType { get; set; }

        [JsonProperty("id")]
        public long? Id { get; set; }

        [JsonProperty("file_name")]
        public string? FileName { get; set; }

        [JsonProperty("content_url")]
        public string? ContentUrl { get; set; }

        [JsonProperty("mapped_content_url")]
        public string? MappedContentUrl { get; set; }

        [JsonProperty("size")]
        public long? Size { get; set; }

        [JsonProperty("url")]
        public string? Url { get; set; }

        [JsonProperty("width")]
        public int? Width { get; set; }

        [JsonProperty("height")]
        public int? Height { get; set; }

        [JsonProperty("inline")]
        public bool? Inline { get; set; }

        [JsonProperty("deleted")]
        public bool? Deleted { get; set; }

        [JsonProperty("malware_access_override")]
        public bool? MalwareAccessOverride { get; set; }

        [JsonProperty("malware_scan_result")]
        public string? MalwareScanResult { get; set; }

        [JsonProperty("thumbnails")]
        public List<AttachmentThumbnail>? Thumbnails { get; set; }
    }

    public class AttachmentThumbnail
    {
        [JsonProperty("content-type")]
        public string? ContentType { get; set; }

        [JsonProperty("id")]
        public long? Id { get; set; }

        [JsonProperty("name")]
        public string? FileName { get; set; }

        [JsonProperty("content_url")]
        public string? ContentUrl { get; set; }

        [JsonProperty("mapped_content_url")]
        public string? MappedContentUrl { get; set; }

        [JsonProperty("size")]
        public long? Size { get; set; }

        [JsonProperty("url")]
        public string? Url { get; set; }

        [JsonProperty("width")]
        public int? Width { get; set; }

        [JsonProperty("height")]
        public int? Height { get; set; }

        [JsonProperty("inline")]
        public bool? Inline { get; set; }

        [JsonProperty("deleted")]
        public bool? Deleted { get; set; }
    }
}
