using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public sealed class RejectedRequestMessage : RequestMessage, IRequestMessageUploads
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("uploads")]
    public List<UploadInfo> Uploads { get; set; } = new List<UploadInfo>();
    
    public override Task ProcessAsync(CancellationToken token)
    {
        throw new NotImplementedException();
    }
}