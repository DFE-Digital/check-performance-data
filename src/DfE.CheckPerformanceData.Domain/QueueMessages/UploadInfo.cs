using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public sealed class UploadInfo
{
    [property: JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [property: JsonPropertyName("id")]
    public Guid Id { get; set; }

    [property: JsonPropertyName("comments")]
    public string? Comments { get; set; }
}