using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class UploadResponseDto
    {
        public UploadDto? Upload { get; set; }
    }

    public sealed class UploadDto
    {
        public string? Token { get; set; }
        public List<AttachmentDto> Attachments { get; set; } = new();
    }
}
