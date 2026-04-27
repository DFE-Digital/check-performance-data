using System;
using System.Collections.Generic;
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketCommentDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = default!;
        public long AuthorId { get; set; }
        public string Body { get; set; } = default!;
        public string HtmlBody { get; set; } = default!;
        public string PlainBody { get; set; } = default!;
        public bool Public { get; set; }
        public List<AttachmentDto> Attachments { get; set; } = default!;
        public long AuditId { get; set; }
        public ViaDto Via { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public CommentMetadataDto Metadata { get; set; } = default!;
    }

    public class TicketCommentsResponseDto
    {
        public List<TicketCommentDto> Comments { get; set; } = default!;
        public string NextPage { get; set; }  = default!;
        public string PreviousPage { get; set; }  = default!;
        public int Count { get; set; }
    }
    public class ViaDto
    {
        public string Channel { get; set; }  = default!;
        public ViaSourceDto Source { get; set; }  = default!;
    }

    public class ViaSourceDto
    {
        public Dictionary<string, object> From { get; set; }  = default!;
        public Dictionary<string, object> To { get; set; }  = default!;
        public string Rel { get; set; }  = default!;
    }

    public class CommentMetadataDto
    {
        public MetadataSystemDto System { get; set; }  = default!;
        public Dictionary<string, object> Custom { get; set; }  = default!;
    }

    public class MetadataSystemDto
    {
        public string Client { get; set; }  = default!;
        public string IpAddress { get; set; }  = default!;
        public string Location { get; set; }  = default!;
        public double Latitude { get; set; }  = default!;
        public double Longitude { get; set; }
    }

    public class AttachmentDto
    {
        public string Url { get; set; }  = default!;
        public long Id { get; set; }
        public string FileName { get; set; }  = default!;
        public string ContentUrl { get; set; }  = default!;
        public string MappedContentUrl { get; set; }  = default!;
        public string ContentType { get; set; }  = default!;
        public long Size { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool Inline { get; set; }
        public bool Deleted { get; set; }
        public bool MalwareAccessOverride { get; set; }
        public string MalwareScanResult { get; set; }  = default!;
        public List<AttachmentThumbnailDto> Thumbnails { get; set; }  = default!;
    }

    public class AttachmentThumbnailDto
    {
        public string Url { get; set; }  = default!;
        public long Id { get; set; }
        public string FileName { get; set; }  = default!;
        public string ContentUrl { get; set; }  = default!;
        public string MappedContentUrl { get; set; }  = default!;
        public string ContentType { get; set; }  = default!;
        public long Size { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool Inline { get; set; }
        public bool Deleted { get; set; }
    }
}
