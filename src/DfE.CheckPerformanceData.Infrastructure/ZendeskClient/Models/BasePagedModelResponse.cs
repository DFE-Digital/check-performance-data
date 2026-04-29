using System;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class BasePagedModelResponse
    {
        public Uri? NextPage { get; init; }
        public Uri? PreviousPage { get; init; }
        public int Count { get; init; }
    }
}
