using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketCommentDto
    {
        public long Id { get; init; }
        public string Type { get; init; } = default!;
        public long AuthorId { get; init; }
        public string Body { get; init; } = default!;
        public string HtmlBody { get; init; } = default!;
        public string PlainBody { get; init; } = default!;
        public bool Public { get; init; }
        public List<AttachmentDto> Attachments { get; init; } = default!;
        public long AuditId { get; init; }
        public ViaDto Via { get; init; } = default!;
        public DateTime CreatedAt { get; init; }
        public CommentMetadataDto Metadata { get; init; } = default!;
    }

    public class TicketCommentsResponseDto
    {
        public List<TicketCommentDto> Comments { get; init; } = default!;
        public string NextPage { get; init; } = default!;
        public string PreviousPage { get; init; } = default!;
        public int Count { get; init; }
    }

    public class ViaDto
    {
        public string Channel { get; init; } = default!;
        public ViaSourceDto Source { get; init; } = default!;
    }

    public class ViaSourceDto
    {
        public Dictionary<string, object> From { get; init; } = default!;
        public Dictionary<string, object> To { get; init; } = default!;
        public string Rel { get; init; } = default!;
    }

    public class CommentMetadataDto
    {
        public MetadataSystemDto System { get; init; } = default!;
        public Dictionary<string, object> Custom { get; init; } = default!;
    }

    public class MetadataSystemDto
    {
        public string Client { get; init; } = default!;
        public string IpAddress { get; init; } = default!;
        public string Location { get; init; } = default!;
        public double Latitude { get; init; } = default!;
        public double Longitude { get; init; }
    }

    public class AttachmentDto
    {
        public string Url { get; init; } = default!;
        public long Id { get; init; }
        public string FileName { get; init; } = default!;
        public string ContentUrl { get; init; } = default!;
        public string MappedContentUrl { get; init; } = default!;
        public string ContentType { get; init; } = default!;
        public long Size { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public bool Inline { get; init; }
        public bool Deleted { get; init; }
        public bool MalwareAccessOverride { get; init; }
        public string MalwareScanResult { get; init; } = default!;
        public List<AttachmentThumbnailDto> Thumbnails { get; init; } = default!;
    }

    public class AttachmentThumbnailDto
    {
        public string Url { get; init; } = default!;
        public long Id { get; init; }
        public string FileName { get; init; } = default!;
        public string ContentUrl { get; init; } = default!;
        public string MappedContentUrl { get; init; } = default!;
        public string ContentType { get; init; } = default!;
        public long Size { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public bool Inline { get; init; }
        public bool Deleted { get; init; }
    }
}
