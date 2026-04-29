using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketCommentDto
    {
        public long Id { get; init; }

        public string? Type { get; init; }

        public long AuthorId { get; init; }

        public string Body { get; init; } = string.Empty;

        public string? HtmlBody { get; init; }

        public string? PlainBody { get; init; }

        public bool Public { get; init; }

        public List<AttachmentDto>? Attachments { get; init; }

        public long AuditId { get; init; }

        public ViaDto? Via { get; init; }

        public DateTime CreatedAt { get; init; }

        public CommentMetadataDto? Metadata { get; init; }
    }

    public class TicketCommentsResponseDto
    {
        public List<TicketCommentDto>? Comments { get; init; }

        public string? NextPage { get; init; }

        public string? PreviousPage { get; init; }

        public int Count { get; init; }
    }

    public class ViaDto
    {
        public string Channel { get; init; } = string.Empty;

        public ViaSourceDto? Source { get; init; }
    }

    public class ViaSourceDto
    {
        public Dictionary<string, object>? From { get; init; }

        public Dictionary<string, object>? To { get; init; }

        public string? Rel { get; init; }
    }

    public class CommentMetadataDto
    {
        public MetadataSystemDto? System { get; init; }

        public Dictionary<string, object>? Custom { get; init; }
    }

    public class MetadataSystemDto
    {
        public string? Client { get; init; }

        public string? IpAddress { get; init; }

        public string? Location { get; init; }

        public double Latitude { get; init; }

        public double Longitude { get; init; }
    }

    public class AttachmentDto
    {
        public string Url { get; init; } = string.Empty;

        public long Id { get; init; }

        public string FileName { get; init; } = string.Empty;

        public string? ContentUrl { get; init; }

        public string? MappedContentUrl { get; init; }

        public string? ContentType { get; init; }

        public long Size { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        public bool Inline { get; init; }

        public bool Deleted { get; init; }

        public bool MalwareAccessOverride { get; init; }

        public string? MalwareScanResult { get; init; }

        public List<AttachmentThumbnailDto>? Thumbnails { get; init; }
    }

    public class AttachmentThumbnailDto
    {
        public string Url { get; init; } = string.Empty;

        public long Id { get; init; }

        public string FileName { get; init; } = string.Empty;

        public string? ContentUrl { get; init; }

        public string? MappedContentUrl { get; init; }

        public string ContentType { get; init; } = string.Empty;

        public long Size { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        public bool Inline { get; init; }

        public bool Deleted { get; init; }
    }
}
