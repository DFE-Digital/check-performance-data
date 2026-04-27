using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class TicketCommentsResponse
    {
        [JsonProperty("comments")]
        public List<TicketComment> Comments { get; set; } =default!;

        [JsonProperty("next_page")]
        public string NextPage { get; set; } = default!;

        [JsonProperty("previous_page")]
        public string PreviousPage { get; set; } = default!;

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class TicketComment
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = default!;

        [JsonProperty("author_id")]
        public long AuthorId { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; } = default!;

        [JsonProperty("html_body")]
        public string HtmlBody { get; set; } = default!;

        [JsonProperty("plain_body")]
        public string PlainBody { get; set; } = default!;

        [JsonProperty("public")]
        public bool Public { get; set; }

        [JsonProperty("attachments")]
        public List<Attachment> Attachments { get; set; } = default!;

        [JsonProperty("audit_id")]
        public long AuditId { get; set; }

        [JsonProperty("via")]
        public Via Via { get; set; } = default!;

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("metadata")]
        public CommentMetadata Metadata { get; set; } = default!;

        // 🔥 Optional: parsed key/value fields from Body
        [JsonIgnore]
        private Dictionary<string, string> _parsedFields = new Dictionary<string, string>();

        [JsonIgnore]
        public Dictionary<string, string> ParsedFields
        {
            get
            {
                if (_parsedFields != null)
                    return _parsedFields;

                if (string.IsNullOrWhiteSpace(Body))
                    return _parsedFields = new Dictionary<string, string>();

                return _parsedFields =
                    Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Split(':', 2))
                        .Where(parts => parts.Length == 2)
                        .ToDictionary(
                            parts => parts[0].Trim(),
                            parts =>
                            {
                                var v = parts[1].Trim();
                                return string.Equals(v, "null", StringComparison.OrdinalIgnoreCase)
                                    ? null
                                    : v;
                            }
                        );
            }
        }
    }


    public class Via
    {
        [JsonProperty("channel")]
        public string Channel { get; set; } = default!;

        [JsonProperty("source")]
        public ViaSource Source { get; set; } = default!;
    }

    public class ViaSource
    {
        [JsonProperty("from")]
        public Dictionary<string, object> From { get; set; }    = default!;

        [JsonProperty("to")]
        public Dictionary<string, object> To { get; set; }   = default!;

        [JsonProperty("rel")]
        public string Rel { get; set; } = default!;
    }


    public class CommentMetadata
    {
        [JsonProperty("system")]
        public MetadataSystem System { get; set; } = default!;

        [JsonProperty("custom")]
        public Dictionary<string, object> Custom { get; set; } = new Dictionary<string, object>();
    }

    public class MetadataSystem
    {
        [JsonProperty("client")]
        public string Client { get; set; } = default!;

        [JsonProperty("ip_address")]
        public string IpAddress { get; set; } = default!;

        [JsonProperty("location")]
        public string Location { get; set; } = default!;

        [JsonProperty("latitude")]
        public double Latitude { get; set; }

        [JsonProperty("longitude")]
        public double Longitude { get; set; }
    }


    public class Attachment
    {
        [JsonProperty("url")]
        public string Url { get; set; } = default!;

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("file_name")]
        public string FileName { get; set; } = default!;

        [JsonProperty("content_url")]
        public string ContentUrl { get; set; } = default!;

        [JsonProperty("mapped_content_url")]
        public string MappedContentUrl { get; set; } = default!;

        [JsonProperty("content_type")]
        public string ContentType { get; set; } = default!;

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("width")]
        public int? Width { get; set; }

        [JsonProperty("height")]
        public int? Height { get; set; }

        [JsonProperty("inline")]
        public bool Inline { get; set; }

        [JsonProperty("deleted")]
        public bool Deleted { get; set; }

        [JsonProperty("malware_access_override")]
        public bool MalwareAccessOverride { get; set; }

        [JsonProperty("malware_scan_result")]
        public string MalwareScanResult { get; set; } = default!;

        [JsonProperty("thumbnails")]
        public List<AttachmentThumbnail>? Thumbnails { get; set; } = new List<AttachmentThumbnail>();
    }

    public class AttachmentThumbnail
    {
        [JsonProperty("url")]
        public string Url { get; set; } = default!;

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("file_name")]
        public string FileName { get; set; } = default!;

        [JsonProperty("content_url")]
        public string ContentUrl { get; set; } = default!;

        [JsonProperty("mapped_content_url")]
        public string MappedContentUrl { get; set; } = default!;

        [JsonProperty("content_type")]
        public string ContentType { get; set; } = default!;

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("width")]
        public int? Width { get; set; }

        [JsonProperty("height")]
        public int? Height { get; set; }

        [JsonProperty("inline")]
        public bool Inline { get; set; }

        [JsonProperty("deleted")]
        public bool Deleted { get; set; }
    }
}
