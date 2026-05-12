using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public abstract class RequestMessage
{
    public Guid WindowId { get; set; }
    public Guid RequestId { get; set; }
    public DecisionType DecisionType { get; set; }
    public abstract Task ProcessAsync(CancellationToken token);
}