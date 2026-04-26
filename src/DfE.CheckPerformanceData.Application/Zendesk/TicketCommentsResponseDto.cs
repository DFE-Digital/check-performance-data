using System;
using System.Collections.Generic;
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketCommentDto
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public long AuthorId { get; set; }
        public string Body { get; set; }
        public string HtmlBody { get; set; }
        public string PlainBody { get; set; }
        public bool Public { get; set; }
        public List<AttachmentDto> Attachments { get; set; }
        public long AuditId { get; set; }
        public ViaDto Via { get; set; }
        public DateTime CreatedAt { get; set; }
        public CommentMetadataDto Metadata { get; set; }
    }

    public class TicketCommentsResponseDto
    {
        public List<TicketCommentDto> Comments { get; set; }
        public string NextPage { get; set; }
        public string PreviousPage { get; set; }
        public int Count { get; set; }
    }
    public class ViaDto
    {
        public string Channel { get; set; }
        public ViaSourceDto Source { get; set; }
    }

    public class ViaSourceDto
    {
        public Dictionary<string, object> From { get; set; }
        public Dictionary<string, object> To { get; set; }
        public string Rel { get; set; }
    }

    public class CommentMetadataDto
    {
        public MetadataSystemDto System { get; set; }
        public Dictionary<string, object> Custom { get; set; }
    }

    public class MetadataSystemDto
    {
        public string Client { get; set; }
        public string IpAddress { get; set; }
        public string Location { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class AttachmentDto
    {
        public string Url { get; set; }
        public long Id { get; set; }
        public string FileName { get; set; }
        public string ContentUrl { get; set; }
        public string MappedContentUrl { get; set; }
        public string ContentType { get; set; }
        public long Size { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool Inline { get; set; }
        public bool Deleted { get; set; }
        public bool MalwareAccessOverride { get; set; }
        public string MalwareScanResult { get; set; }
        public List<AttachmentThumbnailDto> Thumbnails { get; set; }
    }

    public class AttachmentThumbnailDto
    {
        public string Url { get; set; }
        public long Id { get; set; }
        public string FileName { get; set; }
        public string ContentUrl { get; set; }
        public string MappedContentUrl { get; set; }
        public string ContentType { get; set; }
        public long Size { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool Inline { get; set; }
        public bool Deleted { get; set; }
    }
}
