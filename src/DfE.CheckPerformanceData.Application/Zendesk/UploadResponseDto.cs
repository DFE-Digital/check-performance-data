using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class UploadResponse
    {
        public UploadDto Upload { get; set; }
    }

    public class UploadDto
    {
        public string Token { get; set; }

        public List<AttachmentDto> Attachments { get; set; }
    }
}
