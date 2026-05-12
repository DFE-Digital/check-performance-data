namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public interface IRequestMessageUploads
{
    public List<UploadInfo> Uploads { get; set; }
}